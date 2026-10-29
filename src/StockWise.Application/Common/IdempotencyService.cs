using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Common;

/// <summary>
/// Duplicate-request protection for critical posting endpoints. The first call executes the
/// action and stores a response snapshot; retries with the same key return the stored response.
/// Must be called inside a transaction so the record commits atomically with the posted data.
/// </summary>
public class IdempotencyService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public IdempotencyService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<T> ExecuteAsync<T>(string? key, string endpoint, Func<Task<T>> action, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        var userId = _currentUser.UserId ?? throw AppException.Unauthorized();
        if (string.IsNullOrWhiteSpace(key))
            return await action();

        var normalized = key.Trim();
        var existing = await _db.IdempotencyRecords
            .FirstOrDefaultAsync(r => r.TenantId == tenantId && r.Key == normalized && r.Endpoint == endpoint, ct);
        if (existing is not null)
            return JsonSerializer.Deserialize<T>(existing.ResponseSnapshot ?? "null")
                ?? throw AppException.Conflict("This request was already processed with the same idempotency key.");

        var result = await action();
        _db.IdempotencyRecords.Add(new IdempotencyRecord
        {
            TenantId = tenantId, Key = normalized, Endpoint = endpoint,
            ResponseSnapshot = JsonSerializer.Serialize(result), UserId = userId
        });
        await _db.SaveChangesAsync(ct);
        return result;
    }
}
