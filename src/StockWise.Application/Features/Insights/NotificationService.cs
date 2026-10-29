using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Insights;

public record NotificationDto(Guid Id, string Type, string Title, string Message, string Severity,
    string? Link, DateTime CreatedAtUtc, bool IsRead);

public class NotificationService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;

    public NotificationService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    /// <summary>
    /// Sweeps operational conditions into deduplicated notifications. Re-running is cheap:
    /// existing unresolved alerts are refreshed, resolved ones are closed, and no duplicates
    /// are created thanks to the per-tenant unique dedup key.
    /// </summary>
    public async Task SweepAsync(CancellationToken ct)
    {
        var tenantId = TenantId;
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == tenantId, ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(org.TimeZoneId);
        var todayLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;

        var existing = await _db.Notifications.Where(n => n.TenantId == tenantId).ToListAsync(ct);
        var byKey = existing.ToDictionary(n => n.DedupKey);
        var activeKeys = new HashSet<string>();

        async Task UpsertAsync(string type, string dedupKey, string title, string message,
            NotificationSeverity severity, string link)
        {
            activeKeys.Add(dedupKey);
            if (byKey.TryGetValue(dedupKey, out var n) && n.ResolvedAtUtc == null)
            {
                n.Message = message;
                n.Severity = severity;
                return;
            }
            _db.Notifications.Add(new Notification
            {
                TenantId = tenantId, Type = type, DedupKey = dedupKey, Title = title,
                Message = message, Severity = severity, Link = link
            });
        }

        // Low stock / out of stock
        var stockRowsRaw = await (from b in _db.InventoryBalances.AsNoTracking()
                                  join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                                  where b.TenantId == tenantId && p.Status == ProductStatus.Active
                                  select new { b.ProductId, p.Sku, p.Name, b.Quantity, p.ReorderPoint }).ToListAsync(ct);
        var stockRows = stockRowsRaw.GroupBy(r => r.ProductId).Select(g => new
        {
            ProductId = g.Key, g.First().Sku, g.First().Name,
            Quantity = g.Sum(x => x.Quantity), ReorderPoint = g.Max(x => x.ReorderPoint)
        }).ToList();

        foreach (var r in stockRows)
        {
            if (r.Quantity <= 0)
                await UpsertAsync("OutOfStock", $"OutOfStock:{r.ProductId}",
                    $"Out of stock: {r.Name}",
                    $"'{r.Sku}' has no stock left in any warehouse.",
                    NotificationSeverity.Critical, $"/products/{r.ProductId}");
            else if (r.Quantity <= r.ReorderPoint)
                await UpsertAsync("LowStock", $"LowStock:{r.ProductId}",
                    $"Low stock: {r.Name}",
                    $"'{r.Sku}' is at {r.Quantity} (reorder point {r.ReorderPoint}).",
                    NotificationSeverity.Warning, $"/products/{r.ProductId}");
        }

        // Expiring soon / expired
        var expiryRows = await (from b in _db.Batches.AsNoTracking()
                                join p in _db.Products.AsNoTracking() on b.ProductId equals p.Id
                                where b.TenantId == tenantId && b.QuantityRemaining > 0 && b.ExpiryDate != null
                                select new { b.Id, b.BatchNumber, b.ExpiryDate, b.ProductId, p.Sku, p.Name }).ToListAsync(ct);
        foreach (var b in expiryRows)
        {
            var days = (int)(b.ExpiryDate!.Value.Date - todayLocal).TotalDays;
            if (days < 0)
                await UpsertAsync("Expired", $"Expired:{b.Id}", $"Expired stock: {b.Name}",
                    $"Batch '{b.BatchNumber}' of '{b.Sku}' expired on {b.ExpiryDate:yyyy-MM-dd}.",
                    NotificationSeverity.Critical, $"/inventory/batches");
            else if (days <= org.ExpiryAlertDays)
                await UpsertAsync("ExpiringSoon", $"ExpiringSoon:{b.Id}", $"Expiring soon: {b.Name}",
                    $"Batch '{b.BatchNumber}' of '{b.Sku}' expires in {days} day(s).",
                    NotificationSeverity.Warning, $"/inventory/batches");
        }

        // Purchase orders pending approval
        var pendingCount = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(p => p.TenantId == tenantId && p.Status == PurchaseOrderStatus.PendingApproval, ct);
        if (pendingCount > 0)
            await UpsertAsync("PurchaseOrderApproval", $"POApproval:{tenantId}",
                "Purchase orders awaiting approval",
                $"{pendingCount} purchase order(s) are waiting for approval.",
                NotificationSeverity.Info, "/purchase-orders");

        // Overdue expected deliveries
        var overdue = await (from po in _db.PurchaseOrders.AsNoTracking()
                             join s in _db.Suppliers.AsNoTracking() on po.SupplierId equals s.Id
                             where po.TenantId == tenantId
                                   && (po.Status == PurchaseOrderStatus.Ordered || po.Status == PurchaseOrderStatus.PartiallyReceived)
                                   && po.ExpectedDeliveryDate != null
                             select new { po.Number, po.ExpectedDeliveryDate, Supplier = s.Name }).ToListAsync(ct);
        foreach (var po in overdue.Where(p => p.ExpectedDeliveryDate!.Value.Date < todayLocal))
            await UpsertAsync("OverdueDelivery", $"Overdue:{po.Number}",
                $"Delivery overdue: {po.Number}",
                $"Order {po.Number} from {po.Supplier} was expected on {po.ExpectedDeliveryDate:yyyy-MM-dd}.",
                NotificationSeverity.Warning, "/purchase-orders");

        // Close alerts whose condition no longer holds.
        foreach (var n in existing.Where(n => n.ResolvedAtUtc == null && !activeKeys.Contains(n.DedupKey)))
            n.ResolvedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<NotificationDto>> ListAsync(bool? unreadOnly, int limit, CancellationToken ct)
    {
        var tenantId = TenantId;
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();

        var rows = await (from n in _db.Notifications.AsNoTracking()
                          where n.TenantId == tenantId && n.ResolvedAtUtc == null
                          join r in _db.NotificationReads.AsNoTracking().Where(r => r.UserId == uid)
                              on n.Id equals r.NotificationId into reads
                          from read in reads.DefaultIfEmpty()
                          orderby n.Severity descending, n.CreatedAtUtc descending
                          select new { n, IsRead = read != null }).ToListAsync(ct);

        if (unreadOnly == true) rows = rows.Where(x => !x.IsRead).ToList();
        return rows.Take(Math.Clamp(limit, 1, 200))
            .Select(x => new NotificationDto(x.n.Id, x.n.Type, x.n.Title, x.n.Message,
                x.n.Severity.ToString(), x.n.Link, x.n.CreatedAtUtc, x.IsRead))
            .ToList();
    }

    public async Task<int> UnreadCountAsync(CancellationToken ct)
    {
        var tenantId = TenantId;
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var total = await _db.Notifications.AsNoTracking().CountAsync(n => n.TenantId == tenantId && n.ResolvedAtUtc == null, ct);
        var read = await _db.NotificationReads.AsNoTracking()
            .CountAsync(r => r.UserId == uid && _db.Notifications.Any(n => n.Id == r.NotificationId && n.TenantId == tenantId && n.ResolvedAtUtc == null), ct);
        return total - read;
    }

    public async Task MarkReadAsync(Guid id, CancellationToken ct)
    {
        var tenantId = TenantId;
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var exists = await _db.Notifications.AnyAsync(n => n.Id == id && n.TenantId == tenantId, ct);
        if (!exists) throw AppException.NotFound("Notification");
        var already = await _db.NotificationReads.FirstOrDefaultAsync(r => r.NotificationId == id && r.UserId == uid, ct);
        if (already == null)
        {
            _db.NotificationReads.Add(new NotificationRead { NotificationId = id, UserId = uid });
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task MarkAllReadAsync(CancellationToken ct)
    {
        var tenantId = TenantId;
        var uid = _currentUser.UserId ?? throw AppException.Unauthorized();
        var unread = await (from n in _db.Notifications.AsNoTracking()
                            where n.TenantId == tenantId && n.ResolvedAtUtc == null
                            join r in _db.NotificationReads.AsNoTracking().Where(r => r.UserId == uid)
                                on n.Id equals r.NotificationId into reads
                            from read in reads.DefaultIfEmpty()
                            where read == null
                            select n.Id).ToListAsync(ct);
        foreach (var nid in unread)
            _db.NotificationReads.Add(new NotificationRead { NotificationId = nid, UserId = uid });
        if (unread.Count > 0) await _db.SaveChangesAsync(ct);
    }
}
