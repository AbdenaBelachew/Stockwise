using System.Data;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Inventory;

public record PostMovementCommand(
    Guid ProductId,
    Guid WarehouseId,
    decimal QuantityDelta,
    MovementType MovementType,
    decimal? UnitCost,
    Guid? BatchId,
    string? BatchNumber,
    DateTime? ExpiryDate,
    string? ReferenceType,
    Guid? ReferenceId,
    string? ReferenceNumber,
    string? Reason,
    bool AllowNegativeStock,
    int? MinShelfLifeDays);

/// <summary>
/// The single entry point for changing stock. Every stock change in the system goes through
/// PostAsync so that ledger, balance and batch state can never drift apart:
///   - writes an immutable InventoryMovement ledger row,
///   - updates the InventoryBalance projection under SELECT ... FOR UPDATE (serializes concurrent posts),
///   - creates/updates Batch rows for batch-tracked products,
///   - maintains weighted-average cost.
/// Must be called inside an ambient database transaction.
/// </summary>
public class InventoryPostingService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public InventoryPostingService(IAppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<InventoryMovement> PostAsync(PostMovementCommand cmd, CancellationToken ct)
    {
        var tenantId = _currentUser.TenantId ?? throw AppException.Unauthorized();
        var userId = _currentUser.UserId ?? throw AppException.Unauthorized();

        if (cmd.QuantityDelta == 0)
            throw AppException.Validation("Quantity must not be zero.");

        var product = await _db.Products.FirstOrDefaultAsync(p => p.Id == cmd.ProductId && p.TenantId == tenantId, ct)
            ?? throw AppException.NotFound("Product");
        if (product.Status == ProductStatus.Archived)
            throw AppException.Validation($"Product '{product.Sku}' is archived and cannot receive stock movements.");
        if (!await _db.Warehouses.AnyAsync(w => w.Id == cmd.WarehouseId && w.TenantId == tenantId && w.IsActive, ct))
            throw AppException.Validation("Receiving warehouse not found or inactive.",
                new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown or inactive warehouse."] });

        // Fractional quantity guard: integral units must receive integral quantities.
        var unit = await _db.Units.AsNoTracking().FirstAsync(u => u.Id == product.UnitId, ct);
        if (!unit.AllowsFractional && cmd.QuantityDelta != Math.Truncate(cmd.QuantityDelta))
            throw AppException.Validation($"Product '{product.Sku}' uses unit '{unit.Name}' which does not allow fractional quantities.");

        Batch? batch = null;
        if (product.TrackBatches)
        {
            if (cmd.BatchId is { } bid)
            {
                batch = await _db.Batches.FirstOrDefaultAsync(b => b.Id == bid && b.TenantId == tenantId, ct)
                    ?? throw AppException.NotFound("Batch");
            }
            else if (cmd.QuantityDelta > 0)
            {
                if (string.IsNullOrWhiteSpace(cmd.BatchNumber))
                    throw AppException.Validation($"Product '{product.Sku}' is batch-tracked: a batch/lot number is required to add stock.",
                        new Dictionary<string, string[]> { ["batchNumber"] = ["Batch number is required for this product."] });
                if (product.TrackExpiry && cmd.ExpiryDate is null)
                    throw AppException.Validation($"Product '{product.Sku}' is expiry-tracked: an expiry date is required.",
                        new Dictionary<string, string[]> { ["expiryDate"] = ["Expiry date is required for this product."] });

                var batchNumber = cmd.BatchNumber.Trim();
                batch = await _db.Batches.FirstOrDefaultAsync(b =>
                    b.TenantId == tenantId && b.ProductId == product.Id &&
                    b.WarehouseId == cmd.WarehouseId && b.BatchNumber == batchNumber, ct);

                if (batch is null)
                {
                    // Minimum remaining shelf life: refuse short-dated IN movements unless explicitly overridden.
                    if (product.TrackExpiry && cmd.ExpiryDate is { } exp &&
                        product.MinShelfLifeDays is { } min && min > 0)
                    {
                        var today = DateTime.UtcNow.Date;
                        var remaining = (exp.Date - today).Days;
                        if (remaining < min)
                            throw AppException.Validation(
                                $"Batch {batchNumber} expires on {exp:yyyy-MM-dd}, which is below the minimum remaining shelf life of {min} days for this product.",
                                new Dictionary<string, string[]> { ["expiryDate"] = ["Insufficient remaining shelf life."] });
                    }

                    batch = new Batch
                    {
                        TenantId = tenantId,
                        ProductId = product.Id,
                        WarehouseId = cmd.WarehouseId,
                        BatchNumber = batchNumber,
                        ExpiryDate = cmd.ExpiryDate,
                        InitialQuantity = 0,
                        QuantityRemaining = 0,
                        UnitCost = cmd.UnitCost ?? product.CostPrice ?? 0m
                    };
                    _db.Batches.Add(batch);
                    await _db.SaveChangesAsync(ct); // assign id before movement references it
                }
                else if (batch.Status == BatchStatus.Quarantined && cmd.QuantityDelta > 0)
                    throw AppException.Validation($"Batch '{batch.BatchNumber}' is quarantined and cannot receive more stock.");
            }
        }
        else if (cmd.BatchNumber is not null || cmd.ExpiryDate is not null)
        {
            throw AppException.Validation($"Product '{product.Sku}' is not batch-tracked; batch fields must be empty.",
                new Dictionary<string, string[]> { ["batchNumber"] = ["Product is not batch-tracked."] });
        }

        // ---- Lock the balance row (FOR UPDATE) so concurrent postings serialize per (product, warehouse). ----
        var balance = await LockBalanceAsync(tenantId, cmd.ProductId, cmd.WarehouseId, ct);
        var newQuantity = balance.Quantity + cmd.QuantityDelta;

        if (newQuantity < 0 && !cmd.AllowNegativeStock)
            throw AppException.Conflict(
                $"Insufficient stock for '{product.Sku}' in the selected warehouse. Available: {balance.Quantity}, requested: {Math.Abs(cmd.QuantityDelta)}.");

        if (batch is null && cmd.QuantityDelta < 0 && product.TrackBatches)
        {
            batch = await AllocateFefoAsync(tenantId, product.Id, cmd.WarehouseId, Math.Abs(cmd.QuantityDelta), ct);
        }

        if (batch is not null)
        {
            var lockedBatch = await _db.Batches
                .FromSqlInterpolated($@"SELECT * FROM batches WHERE id = {batch.Id} FOR UPDATE")
                .FirstAsync(ct);
            lockedBatch.QuantityRemaining += cmd.QuantityDelta;
            if (lockedBatch.QuantityRemaining < 0) lockedBatch.QuantityRemaining = 0; // policy: batches never go negative
            if (lockedBatch.QuantityRemaining <= 0 && lockedBatch.Status != BatchStatus.Quarantined)
                lockedBatch.Status = BatchStatus.Depleted;
            else if (lockedBatch.Status == BatchStatus.Depleted && lockedBatch.QuantityRemaining > 0)
                lockedBatch.Status = BatchStatus.Active;
            if (cmd.UnitCost is { } uc && cmd.QuantityDelta > 0) lockedBatch.UnitCost = uc;
            batch = lockedBatch;
        }

        // ---- Weighted average cost maintenance on the balance ----
        var avgCost = balance.AvgUnitCost;
        var postCost = cmd.UnitCost;
        if (cmd.QuantityDelta > 0 && cmd.UnitCost is { } inCost)
        {
            var oldQty = balance.Quantity;
            var oldCost = balance.AvgUnitCost == 0 ? inCost : balance.AvgUnitCost;
            avgCost = oldQty + cmd.QuantityDelta == 0 ? inCost
                : (oldQty * oldCost + cmd.QuantityDelta * inCost) / (oldQty + cmd.QuantityDelta);
        }
        else if (cmd.QuantityDelta < 0)
        {
            postCost = balance.AvgUnitCost != 0 ? balance.AvgUnitCost : cmd.UnitCost; // outbound movements carry the current average out
        }

        var movement = new InventoryMovement
        {
            TenantId = tenantId,
            ProductId = cmd.ProductId,
            WarehouseId = cmd.WarehouseId,
            BatchId = batch?.Id,
            MovementType = cmd.MovementType,
            QuantityDelta = cmd.QuantityDelta,
            UnitCost = postCost,
            ReferenceType = cmd.ReferenceType,
            ReferenceId = cmd.ReferenceId,
            ReferenceNumber = cmd.ReferenceNumber,
            Reason = cmd.Reason,
            UserId = userId
        };
        _db.InventoryMovements.Add(movement);

        balance.Quantity = newQuantity;
        balance.AvgUnitCost = avgCost;
        balance.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return movement;
    }

    private async Task<InventoryBalance?> LockBalanceAsync(Guid tenantId, Guid productId, Guid warehouseId, CancellationToken ct)
    {
        // Create the balance row if missing without a race: ON CONFLICT DO NOTHING,
        // then take a row lock so concurrent postings serialize per (product, warehouse).
        await _db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO inventory_balances (id, tenant_id, product_id, warehouse_id, quantity, avg_unit_cost, updated_at_utc)
            VALUES ({Guid.NewGuid()}, {tenantId}, {productId}, {warehouseId}, 0, 0, {DateTime.UtcNow})
            ON CONFLICT (tenant_id, product_id, warehouse_id) DO NOTHING", ct);

        return await _db.InventoryBalances
            // Explicit column list: EF wraps raw SQL in a subquery and projects the mapped
            // xmin concurrency token, which SELECT * does not expose in PostgreSQL.
            .FromSqlInterpolated($@"SELECT id, tenant_id, product_id, warehouse_id, quantity, avg_unit_cost, updated_at_utc, xmin
                                    FROM inventory_balances
                                    WHERE tenant_id = {tenantId} AND product_id = {productId} AND warehouse_id = {warehouseId}
                                    FOR UPDATE")
            .SingleAsync(ct);
    }

    /// <summary>First-Expired-First-Out allocation across available batches of one product+warehouse.</summary>
    private async Task<Batch> AllocateFefoAsync(Guid tenantId, Guid productId, Guid warehouseId, decimal quantity, CancellationToken ct)
    {
        var candidates = await _db.Batches
            .Where(b => b.TenantId == tenantId && b.ProductId == productId && b.WarehouseId == warehouseId
                && b.QuantityRemaining > 0 && b.Status == BatchStatus.Active)
            .OrderBy(b => b.ExpiryDate == null).ThenBy(b => b.ExpiryDate)
            .ToListAsync(ct);
        if (candidates.Sum(b => b.QuantityRemaining) < quantity)
            throw AppException.Conflict("Insufficient unexpired batch stock to fulfill this movement.");

        // Lock all candidate rows; deplete in FEFO order.
        var locked = new List<Batch>();
        foreach (var c in candidates)
        {
            var b = await _db.Batches
                .FromSqlInterpolated($@"SELECT * FROM batches WHERE id = {c.Id} FOR UPDATE")
                .FirstAsync(ct);
            locked.Add(b);
        }

        var remaining = quantity;
        Batch primary = locked[0];
        foreach (var b in locked)
        {
            var take = Math.Min(b.QuantityRemaining, remaining);
            b.QuantityRemaining -= take;
            remaining -= take;
            if (b.QuantityRemaining <= 0 && b.Status != BatchStatus.Quarantined)
                b.Status = BatchStatus.Depleted;
            if (remaining <= 0) break;
        }
        return primary;
    }
}
