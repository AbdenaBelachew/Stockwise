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

public record ReceiptLineRequest(
    Guid PurchaseOrderLineId, decimal QuantityAccepted, decimal QuantityRejected, string? RejectReason,
    string? BatchNumber, DateTime? ExpiryDate);

public record CreateReceiptRequest(
    Guid PurchaseOrderId, Guid WarehouseId, string? Notes, string? IdempotencyKey,
    IReadOnlyList<ReceiptLineRequest> Lines);

public record ReceiptResult(Guid ReceiptId, string Number, Guid PurchaseOrderId, string PurchaseOrderStatus, int LinesPosted);

public record GoodsReceiptDto(
    Guid Id, string Number, Guid PurchaseOrderId, string PurchaseOrderNumber, Guid WarehouseId, string WarehouseName,
    string SupplierName, string? Notes, DateTime CreatedAt, string CreatedByName,
    IReadOnlyList<GoodsReceiptLineDto> Lines);

public record GoodsReceiptLineDto(
    Guid Id, Guid ProductId, string Sku, string ProductName, string UnitSymbol,
    decimal QuantityAccepted, decimal QuantityRejected, string? RejectReason,
    string? BatchNumber, DateTime? ExpiryDate);

public class CreateReceiptValidator : AbstractValidator<CreateReceiptRequest>
{
    public CreateReceiptValidator()
    {
        RuleFor(x => x.PurchaseOrderId).NotEmpty();
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Add at least one receipt line.");
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.PurchaseOrderLineId).NotEmpty();
            l.RuleFor(x => x.QuantityAccepted).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x.QuantityRejected).GreaterThanOrEqualTo(0);
            l.RuleFor(x => x).Must(x => x.QuantityAccepted > 0 || x.QuantityRejected > 0)
                .WithMessage("Each line must record an accepted or rejected quantity.");
        });
    }
}

public class GoodsReceiptService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly InventoryPostingService _posting;
    private readonly IdempotencyService _idempotency;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;

    public GoodsReceiptService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        InventoryPostingService posting, IdempotencyService idempotency, INumberGenerator numbers, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
        _posting = posting; _idempotency = idempotency; _numbers = numbers; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<GoodsReceiptDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.PurchaseOrdersRead, ct);
        var q = _db.GoodsReceipts.AsNoTracking().Where(g => g.TenantId == TenantId)
            .OrderByDescending(g => g.CreatedAt)
            .Select(g => new GoodsReceiptDto(g.Id, g.Number, g.PurchaseOrderId,
                _db.PurchaseOrders.Where(p => p.Id == g.PurchaseOrderId).Select(p => p.Number).FirstOrDefault() ?? "?",
                g.WarehouseId,
                _db.Warehouses.Where(w => w.Id == g.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "?",
                _db.PurchaseOrders.Where(p => p.Id == g.PurchaseOrderId)
                    .Select(p => _db.Suppliers.Where(s => s.Id == p.SupplierId).Select(s => s.Name).FirstOrDefault())
                    .FirstOrDefault() ?? "?",
                g.Notes, g.CreatedAt,
                _db.Users.Where(u => u.Id == g.CreatedBy).Select(u => u.FullName).FirstOrDefault() ?? "system",
                new GoodsReceiptLineDto[] { }));
        return await q.ToPageResultAsync(page, pageSize, ct);
    }

    public async Task<GoodsReceiptDto> GetAsync(Guid id, CancellationToken ct)
    {
        var g = await _db.GoodsReceipts.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Goods receipt");

        var po = await _db.PurchaseOrders.AsNoTracking().FirstAsync(p => p.Id == g.PurchaseOrderId, ct);
        var supplierName = await _db.Suppliers.AsNoTracking().Where(s => s.Id == po.SupplierId).Select(s => s.Name).FirstOrDefaultAsync(ct) ?? "?";
        var warehouseName = await _db.Warehouses.AsNoTracking().Where(w => w.Id == g.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "?";
        var creator = g.CreatedBy is { } cid
            ? await _db.Users.AsNoTracking().Where(u => u.Id == cid).Select(u => u.FullName).FirstOrDefaultAsync(ct) ?? "system"
            : "system";
        var productIds = g.Lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var units = await _db.Units.AsNoTracking().ToListAsync(ct);

        var lines = g.Lines.Select(l =>
        {
            var p = products.FirstOrDefault(x => x.Id == l.ProductId);
            var unit = p is null ? null : units.FirstOrDefault(u => u.Id == p.UnitId);
            return new GoodsReceiptLineDto(l.Id, l.ProductId, p?.Sku ?? "?", p?.Name ?? "?", unit?.Symbol ?? "?",
                l.QuantityAccepted, l.QuantityRejected, l.RejectReason, l.BatchNumber, l.ExpiryDate);
        }).ToList();

        return new GoodsReceiptDto(g.Id, g.Number, g.PurchaseOrderId, po.Number, g.WarehouseId, warehouseName,
            supplierName, g.Notes, g.CreatedAt, creator, lines);
    }

    /// <summary>
    /// Posts a goods receipt: validates outstanding quantities and policies, then atomically
    /// writes receipt rows + inventory movements + batch records and updates the purchase order.
    /// Duplicate submissions with the same idempotency key return the original result.
    /// </summary>
    public async Task<ReceiptResult> CreateAsync(CreateReceiptRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.ReceiptsCreate, ct);
        var tenantId = TenantId;

        return await _idempotency.ExecuteAsync<ReceiptResult>(req.IdempotencyKey, "POST /purchase-orders/{id}/receipts", async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);

            var po = await _db.PurchaseOrders.Include(x => x.Lines)
                .FirstOrDefaultAsync(x => x.Id == req.PurchaseOrderId && x.TenantId == tenantId, ct)
                ?? throw AppException.NotFound("Purchase order");
            // Serialize concurrent receiving against the same order.
            await _db.PurchaseOrders
                .FromSqlInterpolated($"SELECT * FROM purchase_orders WHERE id = {req.PurchaseOrderId} FOR UPDATE")
                .FirstAsync(ct);
            if (po.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.Ordered or PurchaseOrderStatus.PartiallyReceived))
                throw AppException.Conflict(
                    $"Only approved, ordered or partially received orders can receive stock (current status: {po.Status}).");

            var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
            var receivingWarehouseId = req.WarehouseId == Guid.Empty ? po.WarehouseId : req.WarehouseId;
            if (!await _db.Warehouses.AnyAsync(w => w.Id == receivingWarehouseId && w.TenantId == tenantId && w.IsActive, ct))
                throw AppException.Validation("Receiving warehouse not found or inactive.",
                    new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown or inactive warehouse."] });

            var number = await _numbers.NextAsync("GOODS_RECEIPT", ct);
            var receipt = new GoodsReceipt
            {
                TenantId = tenantId, Number = number, PurchaseOrderId = po.Id,
                WarehouseId = receivingWarehouseId, Notes = req.Notes,
                IdempotencyKey = string.IsNullOrWhiteSpace(req.IdempotencyKey) ? null : req.IdempotencyKey.Trim(),
                CreatedBy = uid
            };

            foreach (var lineReq in req.Lines)
            {
                var poLine = po.Lines.FirstOrDefault(l => l.Id == lineReq.PurchaseOrderLineId)
                    ?? throw AppException.Validation("Receipt line does not belong to this purchase order.",
                        new Dictionary<string, string[]> { ["lines"] = ["Unknown order line."] });

                var product = await _db.Products.AsNoTracking().FirstAsync(p => p.Id == poLine.ProductId, ct);
                var outstanding = poLine.OrderedQuantity - poLine.ReceivedQuantity;

                if (lineReq.QuantityAccepted > 0)
                {
                    var maxAccepted = outstanding;
                    if (lineReq.QuantityAccepted > outstanding)
                    {
                        if (!org.AllowOverReceipt)
                            throw AppException.Conflict(
                                $"Line for '{product.Sku}': receiving {lineReq.QuantityAccepted} exceeds the outstanding quantity of {outstanding}. Over-receiving is disabled for this organization.");
                        var tolerance = outstanding * (1 + org.OverReceiptTolerancePercent / 100m);
                        if (lineReq.QuantityAccepted > tolerance)
                            throw AppException.Conflict(
                                $"Line for '{product.Sku}': receiving {lineReq.QuantityAccepted} exceeds the over-receipt tolerance of {org.OverReceiptTolerancePercent:0.##}%.");
                        maxAccepted = lineReq.QuantityAccepted;
                    }

                    // Expiry / shelf-life policy checks (also enforced inside posting for batch creation).
                    if (product.TrackExpiry && lineReq.ExpiryDate is { } exp && product.MinShelfLifeDays is { } min && min > 0)
                    {
                        var remaining = (exp.Date - DateTime.UtcNow.Date).Days;
                        if (remaining < min)
                            throw AppException.Conflict(
                                $"'{product.Sku}': batch expires {exp:yyyy-MM-dd}, below the minimum shelf life of {min} days. " +
                                "An authorized override is required.");
                    }

                    decimal effectiveUnitCost = poLine.UnitCost * (1 - poLine.DiscountPercent / 100m);

                    await _posting.PostAsync(new PostMovementCommand(
                        product.Id, receivingWarehouseId, lineReq.QuantityAccepted, MovementType.GoodsReceipt,
                        effectiveUnitCost, null, lineReq.BatchNumber, lineReq.ExpiryDate,
                        "GoodsReceipt", po.Id, po.Number, $"Goods receipt {number}",
                        false, null), ct);
                }

                receipt.Lines.Add(new GoodsReceiptLine
                {
                    PurchaseOrderLineId = poLine.Id,
                    ProductId = product.Id,
                    QuantityAccepted = lineReq.QuantityAccepted,
                    QuantityRejected = lineReq.QuantityRejected,
                    RejectReason = lineReq.QuantityRejected > 0 ? lineReq.RejectReason : null,
                    BatchNumber = string.IsNullOrWhiteSpace(lineReq.BatchNumber) ? null : lineReq.BatchNumber.Trim(),
                    ExpiryDate = lineReq.ExpiryDate
                });

                poLine.ReceivedQuantity += lineReq.QuantityAccepted;
            }

            _db.GoodsReceipts.Add(receipt);

            // Order status: Received when every line is fully received, PartiallyReceived otherwise.
            var fullyReceived = po.Lines.All(l => l.ReceivedQuantity >= l.OrderedQuantity);
            var partially = po.Lines.Any(l => l.ReceivedQuantity > 0);
            po.Status = fullyReceived ? PurchaseOrderStatus.Received
                : partially ? PurchaseOrderStatus.PartiallyReceived : po.Status;
            if (fullyReceived) po.ClosedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("GoodsReceipt.Posted", "GoodsReceipt", receipt.Id,
                new { receipt.Number, poNumber = po.Number }, ct);
            await tx.CommitAsync(ct);

            return new ReceiptResult(receipt.Id, number, po.Id, po.Status.ToString(), receipt.Lines.Count);
        }, ct);
    }
}
