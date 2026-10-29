using StockWise.Domain.Entities;

namespace StockWise.Application.Abstractions;

/// <summary>The authenticated user and their active tenant for the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string? FullName { get; }
    Guid? TenantId { get; }
    bool IsAuthenticated { get; }
    string? IpAddress { get; }
}

public interface ITenantContext
{
    Guid? TenantId { get; }
    void SetTenant(Guid? tenantId);
}

/// <summary>Validation of the caller's membership and permissions in the active tenant.</summary>
public interface IPermissionChecker
{
    /// <summary>Returns the permission set for the user's membership in the given tenant; empty if no active membership.</summary>
    Task<HashSet<string>> GetPermissionsAsync(Guid userId, Guid tenantId, CancellationToken ct = default);

    /// <summary>Throws AppException.Unauthorized/Forbidden when the user lacks the permission in the tenant.</summary>
    Task EnsurePermissionAsync(Guid userId, Guid tenantId, string permission, CancellationToken ct = default);

    Task EnsureMemberAsync(Guid userId, Guid tenantId, CancellationToken ct = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    (string token, DateTime expiresAtUtc) CreateAccessToken(User user, Guid tenantId, string orgName);
    string NewOpaqueToken();
    string HashToken(string token);
}

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct = default);
}

public interface IAuditService
{
    Task WriteAsync(string action, string entityType, Guid? entityId, object? details = null, CancellationToken ct = default);
}

public interface INumberGenerator
{
    /// <summary>Generates the next document number for a sequence type, e.g. "PO-000123". Must be called inside a transaction.</summary>
    Task<string> NextAsync(string sequenceType, CancellationToken ct = default);
}
