using System.Text;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Insights;

public record DashboardSummaryDto(
    int ActiveProducts, decimal TotalUnits, decimal InventoryValue,
    int LowStockCount, int OutOfStockCount, int ExpiringSoonCount, int ExpiredCount,
    int OpenPurchaseOrders, int PendingApprovalCount,
    int TotalNotifications);

public record TopProductDto(string Sku, string ProductName, decimal MovementQuantity, decimal MovementValue);

public record TrendPointDto(DateTime Date, decimal InboundValue, decimal OutboundValue);

public record DashboardAlertDto(string Type, string Title, string Message, string Severity, string? Link);

public class DashboardService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;

    public DashboardService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);

        var activeProducts = await _db.Products.AsNoTracking().CountAsync(p => p.TenantId == TenantId && p.Status == ProductStatus.Active, ct);
        var balances = await _db.InventoryBalances.AsNoTracking()
            .Where(b => b.TenantId == TenantId).Select(b => new { b.Quantity, b.AvgUnitCost }).ToListAsync(ct);
        var totalUnits = balances.Sum(b => b.Quantity);
        var inventoryValue = balances.Sum(b => b.Quantity * b.AvgUnitCost);

        var productRows = await (from b in _db.InventoryBalances.AsNoTracking()
                                 join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                                 where b.TenantId == TenantId && p.Status == ProductStatus.Active
                                 group new { b.Quantity, p.ReorderPoint } by b.ProductId into g
                                 select new { Quantity = g.Sum(x => x.Quantity), ReorderPoint = g.Max(x => x.ReorderPoint) })
            .ToListAsync(ct);
        var low = productRows.Count(r => r.Quantity > 0 && r.Quantity <= r.ReorderPoint);
        var outOfStock = productRows.Count(r => r.Quantity <= 0);

        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(org.TimeZoneId);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

        var expiryRows = await (from b in _db.Batches.AsNoTracking()
                                where b.TenantId == TenantId && b.QuantityRemaining > 0 && b.ExpiryDate != null
                                select b.ExpiryDate!.Value).ToListAsync(ct);
        var expiring = expiryRows.Count(e => e.Date >= todayLocal && (e.Date - todayLocal).TotalDays <= org.ExpiryAlertDays);
        var expired = expiryRows.Count(e => e.Date < todayLocal);

        var openPOs = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.TenantId == TenantId &&
                (p.Status == PurchaseOrderStatus.Ordered || p.Status == PurchaseOrderStatus.PartiallyReceived), ct);
        var pendingApproval = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.TenantId == TenantId && p.Status == PurchaseOrderStatus.PendingApproval, ct);

        return new DashboardSummaryDto(activeProducts, totalUnits, inventoryValue, low, outOfStock,
            expiring, expired, openPOs, pendingApproval, 0);
    }

    /// <summary>Top-moving products by absolute movement quantity over a date range.</summary>
    public async Task<IReadOnlyList<TopProductDto>> GetTopProductsAsync(DateTime fromUtc, DateTime toUtc, int take, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        fromUtc = DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc);
        toUtc = DateTime.SpecifyKind(toUtc, DateTimeKind.Utc);

        var rows = await (from m in _db.InventoryMovements.AsNoTracking()
                          join p in _db.Products.AsNoTracking() on m.ProductId equals p.Id
                          where m.TenantId == TenantId && m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc <= toUtc
                          group m by new { m.ProductId, p.Sku, p.Name } into g
                          select new TopProductDto(
                              g.Key.Sku, g.Key.Name,
                              g.Sum(x => Math.Abs(x.QuantityDelta)),
                              g.Sum(x => Math.Abs(x.QuantityDelta) * (x.UnitCost ?? 0m))))
            .OrderByDescending(x => x.MovementQuantity)
            .Take(Math.Clamp(take, 1, 25))
            .ToListAsync(ct);
        return rows;
    }

    /// <summary>Daily inbound/outbound movement value for a chart over the requested window.</summary>
    public async Task<IReadOnlyList<TrendPointDto>> GetTrendsAsync(int days, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        days = Math.Clamp(days, 7, 120);
        var fromUtc = DateTime.UtcNow.AddDays(-days);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(
            (await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct)).TimeZoneId);

        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.TenantId == TenantId && m.CreatedAtUtc >= fromUtc)
            .Select(m => new { m.CreatedAtUtc, m.QuantityDelta, m.UnitCost }).ToListAsync(ct);

        return movements
            .GroupBy(m => TimeZoneInfo.ConvertTimeFromUtc(m.CreatedAtUtc, tz).Date)
            .Select(g => new TrendPointDto(
                g.Key,
                g.Where(x => x.QuantityDelta > 0).Sum(x => x.QuantityDelta * (x.UnitCost ?? 0m)),
                g.Where(x => x.QuantityDelta < 0).Sum(x => Math.Abs(x.QuantityDelta) * (x.UnitCost ?? 0m))))
            .OrderBy(p => p.Date)
            .ToList();
    }
}

// ---------------- Reports ----------------

public record ReportRow(string[] Columns);

public record ReportRequest(DateTime? FromUtc, DateTime? ToUtc, Guid? WarehouseId, Guid? ProductId, Guid? SupplierId, int Page = 1, int PageSize = 100);

public record CategoryValueDto(string Category, decimal Value, decimal Quantity);
public record WarehouseValueDto(string Warehouse, decimal Value);
public record PurchaseStatusDto(string Status, int Count, decimal Total);
public record StockHealthDto(int InStock, int LowStock, int OutOfStock);

public record ReportChartsDto(
    IReadOnlyList<CategoryValueDto> ValueByCategory,
    IReadOnlyList<WarehouseValueDto> ValueByWarehouse,
    IReadOnlyList<PurchaseStatusDto> PurchaseStatus,
    StockHealthDto StockHealth,
    DateTime PeriodStartUtc,
    DateTime PeriodEndUtc);

public record PaginatedReport<T>(IReadOnlyList<T> Items, long TotalCount, DateTime? FromUtc, DateTime? ToUtc, Guid? WarehouseId, string Definitions);

public record StockOnHandRow(string Sku, string ProductName, string CategoryName, string WarehouseName,
    string UnitSymbol, decimal Quantity, decimal AvgUnitCost, decimal TotalValue, string StockStatus);

public record ValuationRow(string Sku, string ProductName, string CategoryName, decimal Quantity, decimal AvgUnitCost, decimal TotalValue);

public record MovementReportRow(DateTime CreatedAtUtc, string Sku, string ProductName, string WarehouseName,
    string MovementType, decimal QuantityDelta, decimal? UnitCost, string? ReferenceNumber, string? Reason, string UserName);

public record PurchaseReportRow(string Number, string SupplierName, string Status, string CurrencyCode,
    decimal Total, DateTime CreatedAt, DateTime? ExpectedDeliveryDate, string CreatedBy);

public record AdjustmentReportRow(DateTime CreatedAtUtc, string Sku, string ProductName, string WarehouseName,
    decimal QuantityDelta, string? Reason, string UserName);

public class ReportService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;

    public ReportService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    /// <summary>Aggregates for the reports dashboard charts. Joins are grouped in memory (see note); join-free aggregates stay on the server.</summary>
    public async Task<ReportChartsDto> GetChartsAsync(DateTime? fromUtc, DateTime? toUtc, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var tenantId = TenantId;

        // Inventory value by category (uncategorised grouped under "Uncategorized")
        // and by warehouse. Grouped in memory: grouped aggregates over joined
        // balances do not translate to SQL on EF Core 10 / Npgsql. Row count is
        // bounded by distinct product+warehouse balances, so this stays cheap.
        var balanceRows = await (
            from b in _db.InventoryBalances.AsNoTracking()
            join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
            join c in _db.Categories.AsNoTracking() on p.CategoryId equals c.Id into cats
            from cat in cats.DefaultIfEmpty()
            join w in _db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
            where b.TenantId == tenantId && b.Quantity != 0
            select new { b.Quantity, b.AvgUnitCost, CategoryName = cat.Name, WarehouseName = w.Name })
            .ToListAsync(ct);
        var valueByCategory = balanceRows
            .GroupBy(x => x.CategoryName ?? "Uncategorized")
            .Select(g => new CategoryValueDto(g.Key, g.Sum(x => x.Quantity * x.AvgUnitCost), g.Sum(x => x.Quantity)))
            .OrderByDescending(x => x.Value)
            .ToList();

        var valueByWarehouse = balanceRows
            .GroupBy(x => x.WarehouseName)
            .Select(g => new WarehouseValueDto(g.Key, g.Sum(x => x.Quantity * x.AvgUnitCost)))
            .OrderByDescending(x => x.Value)
            .ToList();

        // Purchase orders by status for the period (default: last 90 days).
        var periodStart = DateTime.SpecifyKind(fromUtc ?? DateTime.UtcNow.AddDays(-90), DateTimeKind.Utc);
        var periodEnd = DateTime.SpecifyKind(toUtc ?? DateTime.UtcNow, DateTimeKind.Utc);
        // Enum-key grouping with ToString() does not translate to SQL; flat rows + in-memory grouping.
        var poRows = await _db.PurchaseOrders.AsNoTracking()
            .Where(po => po.TenantId == tenantId && po.CreatedAt >= periodStart && po.CreatedAt <= periodEnd)
            .Select(po => new { po.Status, po.Total })
            .ToListAsync(ct);
        var purchaseStatus = poRows
            .GroupBy(x => x.Status)
            .Select(g => new PurchaseStatusDto(g.Key.ToString(), g.Count(), g.Sum(x => x.Total)))
            .OrderByDescending(x => x.Total)
            .ToList();

        // Low stock vs healthy stock summary (same join-group limitation as above).
        var healthRows = await (
            from b in _db.InventoryBalances.AsNoTracking()
            join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
            where b.TenantId == tenantId && p.Status == ProductStatus.Active
            select new { b.ProductId, b.Quantity, p.ReorderPoint })
            .ToListAsync(ct);
        var health = healthRows
            .GroupBy(x => x.ProductId)
            .Select(g => (Quantity: g.Sum(x => x.Quantity), ReorderPoint: g.Max(x => x.ReorderPoint)))
            .ToList();
        var inStock = health.Count(r => r.Quantity > r.ReorderPoint);
        var lowStock = health.Count(r => r.Quantity > 0 && r.Quantity <= r.ReorderPoint);
        var outOfStock = health.Count(r => r.Quantity <= 0);

        return new ReportChartsDto(
            valueByCategory, valueByWarehouse, purchaseStatus,
            new StockHealthDto(inStock, lowStock, outOfStock),
            periodStart, periodEnd);
    }

    public async Task<PaginatedReport<StockOnHandRow>> StockOnHandAsync(ReportRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var query = from b in _db.InventoryBalances.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                    join w in _db.Warehouses.AsNoTracking() on b.WarehouseId equals w.Id
                    join u in _db.Units.AsNoTracking() on p.UnitId equals u.Id
                    where b.TenantId == TenantId
                    select new { b, p, w, u };

        if (req.WarehouseId is { } wid) query = query.Where(x => x.b.WarehouseId == wid);
        if (req.ProductId is { } pid) query = query.Where(x => x.b.ProductId == pid);

        var items = await query.OrderBy(x => x.p.Name).ThenBy(x => x.w.Name)
            .Select(x => new StockOnHandRow(x.p.Sku, x.p.Name,
                _db.Categories.Where(c => c.Id == x.p.CategoryId).Select(c => c.Name).FirstOrDefault() ?? "-",
                x.w.Name, x.u.Symbol, x.b.Quantity, x.b.AvgUnitCost, x.b.Quantity * x.b.AvgUnitCost,
                x.b.Quantity <= 0 ? "out" : x.b.Quantity <= x.p.ReorderPoint ? "low" : "in"))
            .ToPageResultAsync(req.Page, req.PageSize, ct);

        var rows = items.Items.ToList();
        return new PaginatedReport<StockOnHandRow>(rows, items.TotalCount, req.FromUtc, req.ToUtc, req.WarehouseId,
            "Quantity = current on-hand balance per product and warehouse (cached projection of the movement ledger). Value = quantity × weighted-average unit cost.");
    }

    public async Task<PaginatedReport<ValuationRow>> InventoryValuationAsync(ReportRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var query = from b in _db.InventoryBalances.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                    where b.TenantId == TenantId && b.Quantity != 0
                    select new { b, p };

        if (req.WarehouseId is { } wid) query = query.Where(x => x.b.WarehouseId == wid);

        // Aggregate to product level for valuation.
        var items = await query.GroupBy(x => new { x.p.Sku, x.p.Name, Category = x.p.CategoryId })
            .Select(g => new ValuationRow(g.Key.Sku, g.Key.Name,
                _db.Categories.Where(c => c.Id == g.Key.Category).Select(c => c.Name).FirstOrDefault() ?? "-",
                g.Sum(x => x.b.Quantity), g.Sum(x => x.b.Quantity * x.b.AvgUnitCost) / (g.Sum(x => x.b.Quantity) == 0 ? 1 : g.Sum(x => x.b.Quantity)),
                g.Sum(x => x.b.Quantity * x.b.AvgUnitCost)))
            .OrderByDescending(x => x.TotalValue)
            .ToPageResultAsync(req.Page, req.PageSize, ct);

        return new PaginatedReport<ValuationRow>(items.Items.ToList(), items.TotalCount, req.FromUtc, req.ToUtc, req.WarehouseId,
            "Inventory valued at weighted-average cost per product, aggregated over warehouses.");
    }

    public async Task<PaginatedReport<MovementReportRow>> MovementsAsync(ReportRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var fromUtc = req.FromUtc ?? DateTime.UtcNow.AddDays(-30);
        var toUtc = req.ToUtc ?? DateTime.UtcNow;

        var query = from m in _db.InventoryMovements.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on m.ProductId equals p.Id
                    join w in _db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id into users
                    from user in users.DefaultIfEmpty()
                    where m.TenantId == TenantId && m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc <= toUtc
                          && (req.WarehouseId == null || m.WarehouseId == req.WarehouseId)
                    select new MovementReportRow(m.CreatedAtUtc, p.Sku, p.Name, w.Name, m.MovementType.ToString(),
                        m.QuantityDelta, m.UnitCost, m.ReferenceNumber, m.Reason, user != null ? user.FullName : "system");

        query = query.OrderByDescending(x => x.CreatedAtUtc);
        var page = await query.ToPageResultAsync(req.Page, req.PageSize, ct);
        return new PaginatedReport<MovementReportRow>(page.Items.ToList(), page.TotalCount, fromUtc, toUtc, req.WarehouseId,
            "Every posted ledger movement in the period, newest first. Movements are immutable.");
    }

    public async Task<PaginatedReport<PurchaseReportRow>> PurchasesAsync(ReportRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var fromUtc = req.FromUtc ?? DateTime.UtcNow.AddDays(-90);
        var toUtc = req.ToUtc ?? DateTime.UtcNow;

        var query = from po in _db.PurchaseOrders.AsNoTracking()
                    join s in _db.Suppliers.AsNoTracking() on po.SupplierId equals s.Id
                    join u in _db.Users.AsNoTracking() on po.CreatedBy equals u.Id into creators
                    from creator in creators.DefaultIfEmpty()
                    where po.TenantId == TenantId && po.CreatedAt >= fromUtc && po.CreatedAt <= toUtc
                          && (req.SupplierId == null || po.SupplierId == req.SupplierId)
                    select new PurchaseReportRow(po.Number, s.Name, po.Status.ToString(), po.CurrencyCode,
                        po.Total, po.CreatedAt, po.ExpectedDeliveryDate, creator != null ? creator.FullName : "system");

        query = query.OrderByDescending(x => x.CreatedAt);
        var page = await query.ToPageResultAsync(req.Page, req.PageSize, ct);
        return new PaginatedReport<PurchaseReportRow>(page.Items.ToList(), page.TotalCount, fromUtc, toUtc, req.SupplierId,
            "Purchase orders created in the period with current status. Totals are in the order currency.");
    }

    public async Task<PaginatedReport<AdjustmentReportRow>> AdjustmentsAsync(ReportRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ReportsRead, ct);
        var fromUtc = req.FromUtc ?? DateTime.UtcNow.AddDays(-30);
        var toUtc = req.ToUtc ?? DateTime.UtcNow;

        var query = from m in _db.InventoryMovements.AsNoTracking()
                    join p in _db.Products.AsNoTracking() on m.ProductId equals p.Id
                    join w in _db.Warehouses.AsNoTracking() on m.WarehouseId equals w.Id
                    join u in _db.Users.AsNoTracking() on m.UserId equals u.Id into users
                    from user in users.DefaultIfEmpty()
                    where m.TenantId == TenantId
                          && (m.MovementType == MovementType.Adjustment || m.MovementType == MovementType.StockCountVariance)
                          && m.CreatedAtUtc >= fromUtc && m.CreatedAtUtc <= toUtc
                    select new AdjustmentReportRow(m.CreatedAtUtc, p.Sku, p.Name, w.Name, m.QuantityDelta,
                        m.Reason, user != null ? user.FullName : "system");

        query = query.OrderByDescending(x => x.CreatedAtUtc);
        var page = await query.ToPageResultAsync(req.Page, req.PageSize, ct);
        return new PaginatedReport<AdjustmentReportRow>(page.Items.ToList(), page.TotalCount, fromUtc, toUtc, req.WarehouseId,
            "Manual adjustments and stock-count variances posted in the period.");
    }

    public static byte[] ToCsv<T>(IEnumerable<T> rows)
    {
        var sb = new StringBuilder();
        var props = typeof(T).GetProperties();
        sb.AppendLine(string.Join(",", props.Select(p => Escape(p.Name))));
        foreach (var r in rows)
            sb.AppendLine(string.Join(",", props.Select(p => Escape(p.GetValue(r)?.ToString() ?? ""))));
        // BOM so Excel opens UTF-8 correctly
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();

        static string Escape(string s) =>
            s.Contains(',') || s.Contains('"') || s.Contains('\n') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}

// ---------------- Settings ----------------

public record SettingsDto(
    string Name, string? Industry, string Country, string TimeZoneId, string CurrencyCode, string DateFormat,
    int ExpiryAlertDays, bool AllowsNegativeStock, bool RequireSeparationOfDuties,
    decimal? ApprovalThresholdAmount, bool AllowOverReceipt, decimal OverReceiptTolerancePercent,
    bool DefaultTrackBatches, bool DefaultTrackExpiry, int MinShelfLifeDays);

public record UpdateSettingsRequest(
    string? TimeZoneId, string? CurrencyCode, string? DateFormat, int? ExpiryAlertDays,
    bool? AllowsNegativeStock, bool? RequireSeparationOfDuties, decimal? ApprovalThresholdAmount,
    bool? AllowOverReceipt, decimal? OverReceiptTolerancePercent, bool? DefaultTrackBatches,
    bool? DefaultTrackExpiry, int? MinShelfLifeDays);

public class SettingsService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly IAuditService _audit;

    public SettingsService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions; _audit = audit;
    }

    public async Task<SettingsDto> GetAsync(CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
        return ToDto(org);
    }

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var tenantId = _currentUser.TenantId!.Value;
        await _permissions.EnsurePermissionAsync(uid, tenantId, Permissions.OrgSettingsManage, ct);
        var org = await _db.Organizations.FirstOrDefaultAsync(o => o.Id == tenantId, ct)
            ?? throw AppException.NotFound("Organization");

        if (req.TimeZoneId is { } tz)
        {
            try { TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { throw AppException.Validation("Unknown time zone.", new Dictionary<string, string[]> { ["timeZoneId"] = ["Unknown IANA time zone."] }); }
            org.TimeZoneId = tz;
        }
        if (req.CurrencyCode is { } cur && cur.Length == 3) org.CurrencyCode = cur.ToUpperInvariant();
        if (req.DateFormat is { } df && !string.IsNullOrWhiteSpace(df)) org.DateFormat = df;
        if (req.ExpiryAlertDays is { } e && e is >= 1 and <= 365) org.ExpiryAlertDays = e;
        if (req.AllowsNegativeStock is { } neg) org.AllowsNegativeStock = neg;
        if (req.RequireSeparationOfDuties is { } sod) org.RequireSeparationOfDuties = sod;
        if (req.ApprovalThresholdAmount is { } thr && thr >= 0) org.ApprovalThresholdAmount = thr == 0 ? null : thr;
        if (req.AllowOverReceipt is { } ovr) org.AllowOverReceipt = ovr;
        if (req.OverReceiptTolerancePercent is { } tol && tol is >= 0 and <= 100) org.OverReceiptTolerancePercent = tol;
        if (req.DefaultTrackBatches is { } tb) org.DefaultTrackBatches = tb;
        if (req.DefaultTrackExpiry is { } te) org.DefaultTrackExpiry = te;
        if (req.MinShelfLifeDays is { } msl && msl >= 0) org.MinShelfLifeDays = msl;

        org.UpdatedAt = DateTime.UtcNow;
        org.UpdatedBy = uid;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Settings.Updated", "Organization", tenantId, req, ct);
        return ToDto(org);
    }

    internal static SettingsDto ToDto(Organization o) => new(
        o.Name, o.Industry, o.Country, o.TimeZoneId, o.CurrencyCode, o.DateFormat,
        o.ExpiryAlertDays, o.AllowsNegativeStock, o.RequireSeparationOfDuties, o.ApprovalThresholdAmount,
        o.AllowOverReceipt, o.OverReceiptTolerancePercent, o.DefaultTrackBatches, o.DefaultTrackExpiry, o.MinShelfLifeDays);
}

// ---------------- Audit log queries ----------------

public record AuditLogDto(Guid Id, string ActorEmail, string Action, string EntityType, string? EntityId,
    string? Details, string? IpAddress, DateTime CreatedAtUtc);

public class AuditQueryService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;

    public AuditQueryService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
    }

    public async Task<PageResult<AuditLogDto>> ListAsync(string? action, string? search, int page, int pageSize, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, tenantId, Permissions.AuditLogsRead, ct);

        var q = _db.AuditLogs.AsNoTracking().Where(a => a.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(action))
            q = q.Where(a => a.Action.ToLower().Contains(action.Trim().ToLower()));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(a => a.ActorEmail.ToLower().Contains(term) || a.EntityType.ToLower().Contains(term));
        }
        var result = await q.OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AuditLogDto(a.Id, a.ActorEmail, a.Action, a.EntityType, a.EntityId, a.Details, a.IpAddress, a.CreatedAtUtc))
            .ToPageResultAsync(page, pageSize, ct);
        return result;
    }
}
