using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Exceptions;
using StockWise.Infrastructure.Persistence;

namespace StockWise.Infrastructure.Services;

public class TenantContext : ITenantContext
{
    public Guid? TenantId { get; private set; }
    public void SetTenant(Guid? tenantId)
    {
        System.Console.WriteLine($"[TenantContext#{GetHashCode():x}] SetTenant({tenantId})");
        TenantId = tenantId;
    }
}

public class PermissionChecker : IPermissionChecker
{
    private readonly StockWiseDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public PermissionChecker(StockWiseDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<HashSet<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var key = $"perms:{userId}:{tenantId}";
        if (_cache.TryGetValue(key, out HashSet<string>? cached) && cached is not null)
            return cached;

        var perms = await (
            from m in _db.TenantMemberships.AsNoTracking()
            join r in _db.Roles.AsNoTracking() on m.RoleId equals r.Id into roles
            from role in roles.DefaultIfEmpty()
            where m.UserId == userId && m.OrganizationId == tenantId && m.Status == Domain.Enums.MembershipStatus.Active
            from p in _db.RolePermissions.AsNoTracking().Where(rp => rp.RoleId == m.RoleId).DefaultIfEmpty()
            select p != null ? p.Permission : null!).ToListAsync(ct);

        var set = perms.Where(p => !string.IsNullOrEmpty(p)).Cast<string>().ToHashSet();
        _cache.Set(key, set, CacheTtl);
        return set;
    }

    public async Task EnsurePermissionAsync(Guid userId, Guid tenantId, string permission, CancellationToken ct = default)
    {
        await EnsureMemberAsync(userId, tenantId, ct);
        var perms = await GetPermissionsAsync(userId, tenantId, ct);
        if (!perms.Contains(permission))
            throw AppException.Forbidden($"Missing required permission: {permission}.");
    }

    public async Task EnsureMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default)
    {
        var member = await _db.TenantMemberships.AsNoTracking()
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == tenantId
                && m.Status == Domain.Enums.MembershipStatus.Active, ct);
        if (member is null)
            throw AppException.Forbidden("You are not an active member of this organization.");
    }
}

public class PasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 11;

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);

    public bool Verify(string password, string hash)
    {
        try { return BCrypt.Net.BCrypt.Verify(password, hash); }
        catch { return false; }
    }
}

public class AuditService : IAuditService
{
    private readonly StockWiseDbContext _db;
    private readonly ICurrentUser _currentUser;

    public AuditService(StockWiseDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task WriteAsync(string action, string entityType, Guid? entityId, object? details = null, CancellationToken ct = default)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            TenantId = _currentUser.TenantId,
            ActorUserId = _currentUser.UserId,
            ActorEmail = _currentUser.Email ?? "anonymous",
            Action = action,
            EntityType = entityType,
            EntityId = entityId?.ToString(),
            Details = details is null ? null : System.Text.Json.JsonSerializer.Serialize(details),
            IpAddress = _currentUser.IpAddress
        });
        // Audit writes must not fail the business operation; persist with the ambient transaction.
        await Task.CompletedTask;
    }
}

public class NumberGenerator : INumberGenerator
{
    private readonly StockWiseDbContext _db;
    private readonly ICurrentUser _currentUser;

    public NumberGenerator(StockWiseDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    private static readonly Dictionary<string, string> Prefixes = new()
    {
        ["PURCHASE_ORDER"] = "PO",
        ["GOODS_RECEIPT"] = "GRN",
        ["TRANSFER"] = "TRF",
        ["STOCK_COUNT"] = "SCN"
    };

    public async Task<string> NextAsync(string sequenceType, CancellationToken ct = default)
    {
        var tenantId = _currentUser.TenantId
            ?? throw AppException.Unauthorized("No active organization.");

        // Row lock guarantees unique, gap-tolerant numbers per tenant + type within the caller's transaction.
        var seq = await _db.NumberSequences
            .FromSqlInterpolated($@"SELECT * FROM number_sequences
                                    WHERE tenant_id = {tenantId} AND sequence_type = {sequenceType}
                                    FOR UPDATE")
            .SingleOrDefaultAsync(ct);

        if (seq is null)
        {
            seq = new NumberSequence { TenantId = tenantId, SequenceType = sequenceType, LastNumber = 0 };
            _db.NumberSequences.Add(seq);
            try { await _db.SaveChangesAsync(ct); }
            catch (DbUpdateException) // concurrent first use: reload under lock
            {
                _db.Entry(seq).State = EntityState.Detached;
                seq = await _db.NumberSequences
                    .FromSqlInterpolated($@"SELECT * FROM number_sequences
                                            WHERE tenant_id = {tenantId} AND sequence_type = {sequenceType}
                                            FOR UPDATE")
                    .SingleAsync(ct);
            }
        }

        seq.LastNumber++;
        var prefix = Prefixes.GetValueOrDefault(sequenceType, sequenceType[..Math.Min(3, sequenceType.Length)].ToUpperInvariant());
        return $"{prefix}-{seq.LastNumber:D6}";
    }
}
