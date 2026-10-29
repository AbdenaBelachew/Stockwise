using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Inventory;

public record BalanceDto(
    Guid ProductId, string Sku, string ProductName, string UnitSymbol, Guid WarehouseId, string WarehouseName,
    decimal Quantity, decimal AvgUnitCost, decimal TotalValue, decimal ReorderPoint, string StockStatus);

public record MovementDto(
    Guid Id, Guid ProductId, string Sku, string ProductName, Guid WarehouseId, string WarehouseName,
    string MovementType, decimal QuantityDelta, decimal? UnitCost, string? ReferenceType, string? ReferenceNumber,
    string? Reason, string? BatchNumber, string UserName, DateTime CreatedAtUtc);

public record AdjustmentLine(
    Guid ProductId, decimal QuantityDelta, Guid? BatchId, string? BatchNumber, DateTime? ExpiryDate,
    string? Reason, decimal? UnitCost = null);

public record CreateAdjustmentRequest(Guid WarehouseId, IReadOnlyList<AdjustmentLine> Lines, string? IdempotencyKey);
public record CreateOpeningBalanceRequest(Guid WarehouseId, IReadOnlyList<AdjustmentLine> Lines, string? IdempotencyKey);

public record AdjustmentResult(int Posted, Guid WarehouseId, string WarehouseName, DateTime PostedAtUtc);

public record ExpiryBatchDto(
    Guid BatchId, Guid ProductId, string Sku, string ProductName, string WarehouseName, string BatchNumber,
    DateTime? ExpiryDate, decimal QuantityRemaining, decimal UnitCost, decimal TotalValue,
    string Status, int? DaysToExpiry, string Bucket);

public record LowStockItemDto(
    Guid ProductId, string Sku, string ProductName, string UnitSymbol, decimal OnHand,
    decimal ReorderPoint, decimal? TargetStockLevel, string StockStatus);

public record MovementListQuery(
    Guid? ProductId, Guid? WarehouseId, string? MovementType, DateTime? FromUtc, DateTime? ToUtc,
    string? Search, int Page = 1, int PageSize = 50);

public class AdjustmentValidator : AbstractValidator<CreateAdjustmentRequest>
{
    public AdjustmentValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty().WithMessage("At least one adjustment line is required.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.QuantityDelta).NotEqual(0);
            line.RuleFor(l => l.Reason).NotEmpty().MaximumLength(500)
                .WithMessage("A reason is required for every adjustment.");
        });
    }
}

public class OpeningBalanceValidator : AbstractValidator<CreateOpeningBalanceRequest>
{
    public OpeningBalanceValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.QuantityDelta).GreaterThan(0);
        });
    }
}

public class InventoryService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly InventoryPostingService _posting;
    private readonly IdempotencyService _idempotency;
    private readonly IAuditService _audit;

    public InventoryService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        InventoryPostingService posting, IdempotencyService idempotency, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
        _posting = posting; _idempotency = idempotency; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<BalanceDto>> GetBalancesAsync(
        Guid? productId, Guid? warehouseId, string? stockStatus, string? search, int page, int pageSize, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);

        var query = from b in _db.InventoryBalances.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                    join w in _db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
                    join u in _db.Units.AsNoTracking() on p.UnitId equals u.Id
                    where b.TenantId == TenantId
                    select new { b, p, w, u };

        if (productId is { } pid) query = query.Where(x => x.b.ProductId == pid);
        if (warehouseId is { } wid) query = query.Where(x => x.b.WarehouseId == wid);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(x => x.p.Name.ToLower().Contains(term) || x.p.Sku.ToLower().Contains(term));
        }

        var projected = query.Select(x => new BalanceRow(
            x.b.ProductId, x.p.Sku, x.p.Name, x.u.Symbol, x.b.WarehouseId, x.w.Name,
            x.b.Quantity, x.b.AvgUnitCost, x.p.ReorderPoint));

        var rows = (await projected.ToListAsync(ct)).AsEnumerable();

        rows = stockStatus?.ToLowerInvariant() switch
        {
            "out" => rows.Where(r => r.Quantity == 0),
            "low" => rows.Where(r => r.Quantity > 0 && r.Quantity <= r.ReorderPoint),
            "in" => rows.Where(r => r.Quantity > r.ReorderPoint),
            _ => rows
        };
        rows = rows.OrderBy(r => r.ProductName).ThenBy(r => r.WarehouseName);

        // Page sizes are bounded: at least 5 rows per page, at most MaxPageSize.
        var effectivePageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 5, QueryableExtensions.MaxPageSize);
        var currentPage = Math.Max(1, page);
        var paged = rows.Skip((currentPage - 1) * effectivePageSize).Take(effectivePageSize).ToList();

        var items = paged.Select(r => new BalanceDto(
            r.ProductId, r.Sku, r.ProductName, r.UnitSymbol, r.WarehouseId, r.WarehouseName,
            r.Quantity, r.AvgUnitCost, r.Quantity * r.AvgUnitCost, r.ReorderPoint,
            r.Quantity <= 0 ? "out" : r.Quantity <= r.ReorderPoint ? "low" : "in")).ToList();

        return new PageResult<BalanceDto> { Items = items, Page = currentPage, PageSize = effectivePageSize, TotalCount = rows.Count() };
    }

    public async Task<PageResult<MovementDto>> GetMovementsAsync(MovementListQuery q, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);

        var query = from m in _db.InventoryMovements.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on m.ProductId equals p.Id
                    join w in _db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id into users
                    from user in users.DefaultIfEmpty()
                    where m.TenantId == TenantId
                    orderby m.CreatedAtUtc descending
                    select new { m, BatchId = m.BatchId, Sku = p.Sku, ProductName = p.Name, WarehouseName = w.Name, UserName = user != null ? user.FullName : "system" };

        if (q.ProductId is { } pid) query = query.Where(x => x.m.ProductId == pid);
        if (q.WarehouseId is { } wid) query = query.Where(x => x.m.WarehouseId == wid);
        if (q.FromUtc is { } from) query = query.Where(x => x.m.CreatedAtUtc >= from);
        if (q.ToUtc is { } to) query = query.Where(x => x.m.CreatedAtUtc <= to);
        if (!string.IsNullOrWhiteSpace(q.MovementType) && Enum.TryParse<MovementType>(q.MovementType, true, out var mt))
            query = query.Where(x => x.m.MovementType == mt);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            query = query.Where(x => x.m.ReferenceNumber != null && x.m.ReferenceNumber.ToLower().Contains(term)
                || x.m.Reason != null && x.m.Reason.ToLower().Contains(term)
                || x.Sku.ToLower().Contains(term));
        }

        var pageResult = await query
            .ToPageResultAsync(q.Page, q.PageSize, ct);

        // Resolve batch numbers for the current page in a second query (keeps the main query translatable).
        var batchIds = pageResult.Items.Where(x => x.BatchId != null).Select(x => x.BatchId!.Value).Distinct().ToList();
        var batchNumbers = batchIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.BatchNumber, ct);

        var items = pageResult.Items.Select(x => new MovementDto(
            x.m.Id, x.m.ProductId, x.Sku, x.ProductName, x.m.WarehouseId, x.WarehouseName,
            x.m.MovementType.ToString(), x.m.QuantityDelta, x.m.UnitCost, x.m.ReferenceType,
            x.m.ReferenceNumber, x.m.Reason,
            x.BatchId is { } bid ? batchNumbers.GetValueOrDefault(bid) : null,
            x.UserName, x.m.CreatedAtUtc)).ToList();

        return new PageResult<MovementDto>
        {
            Items = items, Page = pageResult.Page, PageSize = pageResult.PageSize, TotalCount = pageResult.TotalCount
        };
    }

    public async Task<AdjustmentResult> CreateAdjustmentAsync(CreateAdjustmentRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryAdjust, ct);
        var tenantId = TenantId;
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);

        return await _idempotency.ExecuteAsync<AdjustmentResult>(req.IdempotencyKey, "POST /inventory/adjustments", async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var warehouse = await _db.Warehouses.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == req.WarehouseId && w.TenantId == tenantId, ct)
                ?? throw AppException.Validation("Warehouse not found.",
                    new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown warehouse."] });

            foreach (var line in req.Lines)
            {
                await _posting.PostAsync(new PostMovementCommand(
                    line.ProductId, req.WarehouseId, line.QuantityDelta, MovementType.Adjustment,
                    null, line.BatchId, line.BatchNumber, line.ExpiryDate,
                    "Adjustment", null, null, line.Reason ?? "Stock adjustment",
                    org.AllowsNegativeStock, null), ct);
            }

            await _audit.WriteAsync("Inventory.Adjusted", "Warehouse", req.WarehouseId,
                new { lines = req.Lines.Count }, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new AdjustmentResult(req.Lines.Count, req.WarehouseId, warehouse.Name, DateTime.UtcNow);
        }, ct);
    }

    public async Task<AdjustmentResult> CreateOpeningBalanceAsync(CreateOpeningBalanceRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryAdjust, ct);
        var tenantId = TenantId;

        return await _idempotency.ExecuteAsync<AdjustmentResult>(req.IdempotencyKey, "POST /inventory/opening-balances", async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            var warehouse = await _db.Warehouses.AsNoTracking()
                .FirstOrDefaultAsync(w => w.Id == req.WarehouseId && w.TenantId == tenantId, ct)
                ?? throw AppException.Validation("Warehouse not found.",
                    new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown warehouse."] });

            foreach (var line in req.Lines)
            {
                var product = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId && p.TenantId == tenantId, ct)
                    ?? throw AppException.NotFound("Product");
                await _posting.PostAsync(new PostMovementCommand(
                    line.ProductId, req.WarehouseId, Math.Abs(line.QuantityDelta), MovementType.OpeningBalance,
                    line.UnitCost ?? product.CostPrice, line.BatchId, line.BatchNumber, line.ExpiryDate,
                    "OpeningBalance", null, null, "Opening balance",
                    false, null), ct);
            }

            await _audit.WriteAsync("Inventory.OpeningBalance", "Warehouse", req.WarehouseId,
                new { lines = req.Lines.Count }, ct);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return new AdjustmentResult(req.Lines.Count, req.WarehouseId, warehouse.Name, DateTime.UtcNow);
        }, ct);
    }

    public async Task<IReadOnlyList<LowStockItemDto>> GetLowStockAsync(Guid? warehouseId, bool includeOutOfStock, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);

        var rows = await (from b in _db.InventoryBalances.AsNoTracking()
                          join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                          join u in _db.Units.AsNoTracking() on p.UnitId equals u.Id
                          where b.TenantId == TenantId
                                && (warehouseId == null || b.WarehouseId == warehouseId)
                                && p.Status == ProductStatus.Active
                                && (includeOutOfStock ? b.Quantity <= p.ReorderPoint
                                    : b.Quantity > 0 && b.Quantity <= p.ReorderPoint)
                          orderby b.Quantity
                          select new LowStockItemDto(
                              p.Id, p.Sku, p.Name, u.Symbol, b.Quantity, p.ReorderPoint,
                              p.TargetStockLevel, b.Quantity <= 0 ? "out" : "low"))
            .ToListAsync(ct);
        return rows;
    }

    public async Task<IReadOnlyList<ExpiryBatchDto>> GetExpiryAsync(
        Guid? warehouseId, int? daysAhead, bool includeExpired, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);
        var tenantId = TenantId;
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(org.TimeZoneId);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

        var rows = await (from b in _db.Batches.AsNoTracking()
                          join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                          join w in _db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
                          where b.TenantId == tenantId && b.QuantityRemaining > 0
                                && (warehouseId == null || b.WarehouseId == warehouseId)
                                && p.TrackExpiry && b.ExpiryDate != null
                          orderby b.ExpiryDate
                          select new
                          {
                              b.Id, b.ProductId, p.Sku, ProductName = p.Name, WarehouseName = w.Name,
                              b.BatchNumber, b.ExpiryDate, b.QuantityRemaining, b.UnitCost, b.Status
                          }).ToListAsync(ct);

        var result = new List<ExpiryBatchDto>();
        foreach (var r in rows)
        {
            var days = (int)(r.ExpiryDate!.Value.Date - todayLocal).TotalDays;
            var expiredByPolicy = days < 0; // expiry date is valid through its calendar day; expired the next day
            if (!includeExpired && expiredByPolicy) continue;
            if (daysAhead is { } da && !expiredByPolicy && days > da) continue;

            var bucket = ExpiryPolicy.Classify(days, org.ExpiryAlertDays);

            result.Add(new ExpiryBatchDto(r.Id, r.ProductId, r.Sku, r.ProductName, r.WarehouseName,
                r.BatchNumber, r.ExpiryDate, r.QuantityRemaining, r.UnitCost, r.QuantityRemaining * r.UnitCost,
                r.Status.ToString(), days, bucket));
        }
        return result;
    }

    public async Task QuarantineBatchAsync(Guid batchId, string? note, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.BatchesManage, ct);
        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Batch");
        batch.Status = BatchStatus.Quarantined;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Batch.Quarantined", "Batch", batchId, new { note }, ct);
    }

    public async Task ReleaseBatchAsync(Guid batchId, string? note, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.BatchesManage, ct);
        var batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == batchId && b.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Batch");
        batch.Status = batch.QuantityRemaining > 0 ? BatchStatus.Active : BatchStatus.Depleted;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Batch.Released", "Batch", batchId, new { note }, ct);
    }

    public record BalanceRow(Guid ProductId, string Sku, string ProductName, string UnitSymbol,
        Guid WarehouseId, string WarehouseName, decimal Quantity, decimal AvgUnitCost, decimal ReorderPoint);
}
