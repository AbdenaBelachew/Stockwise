using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Application.Features.Inventory;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Procurement;

// ---------------- DTOs ----------------

public record PurchaseOrderLineDto(
    Guid Id, Guid ProductId, string Sku, string ProductName, string UnitSymbol,
    decimal OrderedQuantity, decimal ReceivedQuantity, decimal UnitCost,
    decimal DiscountPercent, decimal TaxPercent, decimal LineTotal, string? Notes);

public record PurchaseApprovalDto(string Action, string ActorName, string? Reason, DateTime AtUtc);

public record PurchaseOrderDto(
    Guid Id, string Number, Guid SupplierId, string SupplierName, Guid WarehouseId, string WarehouseName,
    string Status, string CurrencyCode, DateTime? ExpectedDeliveryDate, string? Notes,
    decimal SubTotal, decimal TaxTotal, decimal Total, DateTime CreatedAt, string CreatedByName,
    DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? ClosedAt,
    IReadOnlyList<PurchaseOrderLineDto> Lines, IReadOnlyList<PurchaseApprovalDto> History);

public record SavePurchaseOrderRequest(
    Guid SupplierId, Guid WarehouseId, DateTime? ExpectedDeliveryDate, string? Notes,
    IReadOnlyList<SavePurchaseOrderLine> Lines);

public record SavePurchaseOrderLine(Guid ProductId, decimal Quantity, decimal UnitCost,
    decimal DiscountPercent = 0, decimal TaxPercent = 0, string? Notes = null);

public record RejectRequest(string Reason);

public class SavePurchaseOrderValidator : AbstractValidator<SavePurchaseOrderRequest>
{
    public SavePurchaseOrderValidator()
    {
        RuleFor(x => x.SupplierId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one line item.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
            l.RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.DiscountPercent).InclusiveBetween(0, 100);
            l.RuleFor(x => x.TaxPercent).InclusiveBetween(0, 100);
        });
    }
}

public class RejectRequestValidator : AbstractValidator<RejectRequest>
{
    public RejectRequestValidator() => RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
}

public class PurchaseOrderService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;

    public PurchaseOrderService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        INumberGenerator numbers, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions; _numbers = numbers; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<PurchaseOrderDto>> ListAsync(string? status, Guid? supplierId, string? search, int page, int pageSize, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.PurchaseOrdersRead, ct);
        var q = from po in _db.PurchaseOrders.AsNoTracking()
                where po.TenantId == TenantId
                select new
                {
                    po,
                    SupplierName = _db.Suppliers.Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefault(),
                    WarehouseName = _db.Warehouses.Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefault(),
                    CreatorName = _db.Users.Where(u => u.Id == po.CreatedBy).Select(u => u.FullName).FirstOrDefault()
                };

        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<PurchaseOrderStatus>(status, true, out var st))
            q = q.Where(x => x.po.Status == st);
        if (supplierId is { } sid) q = q.Where(x => x.po.SupplierId == sid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(x => x.po.Number.ToLower().Contains(term)
                || (x.SupplierName != null && x.SupplierName.ToLower().Contains(term)));
        }

        var pageResult = await q.OrderByDescending(x => x.po.CreatedAt)
            .Select(x => new PurchaseOrderDto(x.po.Id, x.po.Number, x.po.SupplierId, x.SupplierName ?? "?",
                x.po.WarehouseId, x.WarehouseName ?? "?", x.po.Status.ToString(), x.po.CurrencyCode,
                x.po.ExpectedDeliveryDate, x.po.Notes, x.po.SubTotal, x.po.TaxTotal, x.po.Total,
                x.po.CreatedAt, x.CreatorName ?? "system", x.po.SubmittedAt, x.po.ApprovedAt, x.po.ClosedAt,
                new PurchaseOrderLineDto[] { }, new PurchaseApprovalDto[] { }))
            .ToPageResultAsync(page, pageSize, ct);
        return pageResult;
    }

    public async Task<PurchaseOrderDto> GetAsync(Guid id, CancellationToken ct)
    {
        var po = await _db.PurchaseOrders.AsNoTracking()
            .Include(x => x.Lines).Include(x => x.Approvals)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");

        var supplierName = await _db.Suppliers.AsNoTracking().Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "?";
        var warehouseName = await _db.Warehouses.AsNoTracking().Where(w => w.Id == po.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "?";
        var productIds = po.Lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var units = await _db.Units.AsNoTracking().ToListAsync(ct);
        var actorIds = po.Approvals.Select(a => a.ActorUserId).Distinct().ToList();
        var actors = await _db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var creatorName = po.CreatedBy is { } cid
            ? await _db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "system"
            : "system";

        var lines = po.Lines.Select(l =>
        {
            var p = products.FirstOrDefault(x => x.Id == l.ProductId);
            var unit = p is null ? null : units.FirstOrDefault(u => u.Id == p.UnitId);
            return new PurchaseOrderLineDto(l.Id, l.ProductId, p?.Sku ?? "?", p?.Name ?? "?", unit?.Symbol ?? "?",
                l.OrderedQuantity, l.ReceivedQuantity, l.UnitCost, l.DiscountPercent, l.TaxPercent, l.LineTotal, l.Notes);
        }).ToList();

        var history = po.Approvals.OrderBy(a => a.AtUtc)
            .Select(a => new PurchaseApprovalDto(a.Action.ToString(), actors.GetValueOrDefault(a.ActorUserId, "?"), a.Reason, a.AtUtc))
            .ToList();

        return new PurchaseOrderDto(po.Id, po.Number, po.SupplierId, supplierName, po.WarehouseId, warehouseName,
            po.Status.ToString(), po.CurrencyCode, po.ExpectedDeliveryDate, po.Notes,
            po.SubTotal, po.TaxTotal, po.Total, po.CreatedAt, creatorName,
            po.SubmittedAt, po.ApprovedAt, po.ClosedAt, lines, history);
    }

    public async Task<SimpleResult> CreateAsync(SavePurchaseOrderRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersCreate, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var supplier = await _db.Suppliers.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == req.SupplierId && s.TenantId == TenantId && s.IsActive, ct)
            ?? throw AppException.Validation("Supplier not found or inactive.",
                new Dictionary<string, string[]> { ["supplierId"] = ["Unknown or inactive supplier."] });
        var warehouse = await _db.Warehouses.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == req.WarehouseId && w.TenantId == TenantId, ct)
            ?? throw AppException.Validation("Warehouse not found.",
                new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown warehouse."] });
        await ValidateLinesAsync(req.Lines, ct);

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct);
        var number = await _numbers.NextAsync("PURCHASE_ORDER", ct);
        var po = new PurchaseOrder
        {
            TenantId = TenantId, Number = number, SupplierId = req.SupplierId, WarehouseId = req.WarehouseId,
            CurrencyCode = org.CurrencyCode, ExpectedDeliveryDate = req.ExpectedDeliveryDate?.ToUniversalTime(),
            Notes = req.Notes, CreatedBy = uid
        };
        po.Lines.AddRange(req.Lines.Select(l => new PurchaseOrderLine
        {
            ProductId = l.ProductId, OrderedQuantity = l.Quantity, UnitCost = l.UnitCost,
            DiscountPercent = l.DiscountPercent, TaxPercent = l.TaxPercent, Notes = l.Notes
        }));
        ApplyLines(po, req.Lines);
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Created", "PurchaseOrder", po.Id, new { po.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    /// <summary>Editing is allowed only in Draft. Posted quantities/totals are recomputed server-side.</summary>
    public async Task<SimpleResult> UpdateAsync(Guid id, SavePurchaseOrderRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersCreate, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = await _db.PurchaseOrders.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status != PurchaseOrderStatus.Draft)
            throw AppException.Conflict("Only draft purchase orders can be edited.");
        await ValidateLinesAsync(req.Lines, ct);

        po.SupplierId = req.SupplierId;
        po.WarehouseId = req.WarehouseId;
        po.ExpectedDeliveryDate = req.ExpectedDeliveryDate?.ToUniversalTime();
        po.Notes = req.Notes;
        po.Lines.Clear();
        po.Lines.AddRange(req.Lines.Select(l => new PurchaseOrderLine
        {
            ProductId = l.ProductId, OrderedQuantity = l.Quantity, UnitCost = l.UnitCost,
            DiscountPercent = l.DiscountPercent, TaxPercent = l.TaxPercent, Notes = l.Notes
        }));
        ApplyLines(po, req.Lines);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Updated", "PurchaseOrder", id, new { po.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    /// <summary>Submits for approval. Under the organization's approval threshold the order auto-approves.</summary>
    public async Task<SimpleResult> SubmitAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersSubmit, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status != PurchaseOrderStatus.Draft)
            throw AppException.Conflict($"Only draft orders can be submitted (current status: {po.Status}).");

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct);
        po.Status = PurchaseOrderStatus.PendingApproval;
        po.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        if (org.ApprovalThresholdAmount is { } threshold && threshold > 0 && po.Total <= threshold)
        {
            po.Status = PurchaseOrderStatus.Approved;
            po.ApprovedAt = DateTime.UtcNow;
            _db.PurchaseApprovals.Add(new PurchaseApproval
            {
                PurchaseOrderId = po.Id, Action = ApprovalAction.Approved, ActorUserId = uid,
                Reason = $"Auto-approved under threshold {threshold:0.##}", AtUtc = DateTime.UtcNow
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Submitted", "PurchaseOrder", po.Id, new { po.Number, po.Total }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    public async Task<SimpleResult> ApproveAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersApprove, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status != PurchaseOrderStatus.PendingApproval)
            throw AppException.Conflict($"Only pending orders can be approved (current status: {po.Status}).");

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct);
        if (org.RequireSeparationOfDuties && po.CreatedBy == uid)
            throw AppException.Forbidden("Separation of duties: you cannot approve a purchase order you created.");

        po.Status = PurchaseOrderStatus.Approved;
        po.ApprovedAt = DateTime.UtcNow;
        _db.PurchaseApprovals.Add(new PurchaseApproval
        {
            PurchaseOrderId = po.Id, Action = ApprovalAction.Approved, ActorUserId = uid, AtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Approved", "PurchaseOrder", po.Id, new { po.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    public async Task<SimpleResult> RejectAsync(Guid id, RejectRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersApprove, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status != PurchaseOrderStatus.PendingApproval)
            throw AppException.Conflict($"Only pending orders can be rejected (current status: {po.Status}).");

        po.Status = PurchaseOrderStatus.Rejected;
        po.ClosedAt = DateTime.UtcNow;
        _db.PurchaseApprovals.Add(new PurchaseApproval
        {
            PurchaseOrderId = po.Id, Action = ApprovalAction.Rejected, ActorUserId = uid,
            Reason = req.Reason, AtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Rejected", "PurchaseOrder", po.Id, new { po.Number, req.Reason }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    public async Task<SimpleResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersCancel, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.Cancelled or PurchaseOrderStatus.Rejected)
            throw AppException.Conflict($"A {po.Status} order cannot be cancelled.");

        po.Status = PurchaseOrderStatus.Cancelled;
        po.ClosedAt = DateTime.UtcNow;
        _db.PurchaseApprovals.Add(new PurchaseApproval
        {
            PurchaseOrderId = po.Id, Action = ApprovalAction.Cancelled, ActorUserId = uid, AtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Cancelled", "PurchaseOrder", po.Id, new { po.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    /// <summary>Marks an approved order as sent to the supplier (Ordered). Stock is NOT affected.</summary>
    public async Task<SimpleResult> MarkOrderedAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.PurchaseOrdersSubmit, ct);
        var po = await _db.PurchaseOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Purchase order");
        if (po.Status != PurchaseOrderStatus.Approved)
            throw AppException.Conflict($"Only approved orders can be marked as ordered (current status: {po.Status}).");
        po.Status = PurchaseOrderStatus.Ordered;
        _db.PurchaseApprovals.Add(new PurchaseApproval
        {
            PurchaseOrderId = po.Id, Action = ApprovalAction.Ordered, ActorUserId = uid, AtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("PurchaseOrder.Ordered", "PurchaseOrder", po.Id, new { po.Number }, ct);
        return new SimpleResult(po.Id, po.Number, po.Status.ToString());
    }

    private async Task ValidateLinesAsync(IReadOnlyList<SavePurchaseOrderLine> lines, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var product = await _db.Products.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == line.ProductId && p.TenantId == TenantId, ct)
                ?? throw AppException.Validation("Unknown product in order lines.",
                    new Dictionary<string, string[]> { ["lines"] = [$"Product {line.ProductId} not found."] });
            if (product.Status == ProductStatus.Archived)
                throw AppException.Validation($"Product '{product.Sku}' is archived and cannot be ordered.",
                    new Dictionary<string, string[]> { ["lines"] = [$"Product '{product.Sku}' is archived."] });
        }
    }

    private static void ApplyLines(PurchaseOrder po, IReadOnlyList<SavePurchaseOrderLine> lines)
    {
        // PO lines are created/updated by callers before this is invoked; totals recomputed here.
        var sub = po.Lines.Sum(l => l.OrderedQuantity * l.UnitCost * (1 - l.DiscountPercent / 100m));
        var tax = po.Lines.Sum(l => l.OrderedQuantity * l.UnitCost * (1 - l.DiscountPercent / 100m) * (l.TaxPercent / 100m));
        foreach (var l in po.Lines)
            l.LineTotal = Math.Round(l.OrderedQuantity * l.UnitCost * (1 - l.DiscountPercent / 100m) * (1 + l.TaxPercent / 100m), 4);
        po.SubTotal = Math.Round(sub, 4);
        po.TaxTotal = Math.Round(tax, 4);
        po.Total = po.SubTotal + po.TaxTotal;
    }
}
