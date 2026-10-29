using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Inventory;

// ---------------- Transfers ----------------

public record TransferLineDto(Guid ProductId, string Sku, string ProductName, string UnitSymbol,
    Guid? BatchId, string? BatchNumber, decimal Quantity);

public record TransferDto(
    Guid Id, string Number, Guid SourceWarehouseId, string SourceWarehouseName,
    Guid DestinationWarehouseId, string DestinationWarehouseName, string Status,
    string? Notes, DateTime CreatedAt, DateTime? DispatchedAt, DateTime? ReceivedAt,
    IReadOnlyList<TransferLineDto> Lines);

public record CreateTransferRequest(
    Guid SourceWarehouseId, Guid DestinationWarehouseId, string? Notes,
    IReadOnlyList<CreateTransferLine> Lines, string? IdempotencyKey);

public record CreateTransferLine(Guid ProductId, Guid? BatchId, decimal Quantity);

public record SimpleResult(Guid Id, string Number, string Status);

public class CreateTransferValidator : AbstractValidator<CreateTransferRequest>
{
    public CreateTransferValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(l =>
        {
            l.RuleFor(x => x.ProductId).NotEmpty();
            l.RuleFor(x => x.Quantity).GreaterThan(0);
        });
    }
}

public class TransferService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly InventoryPostingService _posting;
    private readonly IdempotencyService _idempotency;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;

    public TransferService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        InventoryPostingService posting, IdempotencyService idempotency, INumberGenerator numbers, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
        _posting = posting; _idempotency = idempotency; _numbers = numbers; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<TransferDto>> ListAsync(string? status, int page, int pageSize, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);
        var q = _db.StockTransfers.AsNoTracking().Where(t => t.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<TransferStatus>(status, true, out var st))
            q = q.Where(t => t.Status == st);
        var q2 = q.OrderByDescending(t => t.CreatedAt)
            .Select(t => new TransferDto(t.Id, t.Number, t.SourceWarehouseId,
                _db.Warehouses.Where(w => w.Id == t.SourceWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "?",
                t.DestinationWarehouseId,
                _db.Warehouses.Where(w => w.Id == t.DestinationWarehouseId).Select(w => w.Name).FirstOrDefault() ?? "?",
                t.Status.ToString(), t.Notes, t.CreatedAt, t.DispatchedAt, t.ReceivedAt, new TransferLineDto[] { }));
        return await q2.ToPageResultAsync(page, pageSize, ct);
    }

    public async Task<TransferDto> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await _db.StockTransfers.AsNoTracking()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Transfer");
        return await ToDtoAsync(t, ct);
    }

    public async Task<SimpleResult> CreateAsync(CreateTransferRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryTransfer, ct);
        if (req.SourceWarehouseId == req.DestinationWarehouseId)
            throw AppException.Validation("Source and destination warehouses must differ.",
                new Dictionary<string, string[]> { ["destinationWarehouseId"] = ["Cannot transfer to the same warehouse."] });

        return await _idempotency.ExecuteAsync<SimpleResult>(req.IdempotencyKey, "POST /inventory/transfers", async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(ct);
            await EnsureWarehouseAsync(req.SourceWarehouseId, ct);
            await EnsureWarehouseAsync(req.DestinationWarehouseId, ct);

            foreach (var line in req.Lines)
            {
                var product = await _db.Products.AsNoTracking()
                    .FirstOrDefaultAsync(p => p.Id == line.ProductId && p.TenantId == TenantId, ct)
                    ?? throw AppException.NotFound("Product");
                if (product.TrackBatches && line.BatchId is null)
                    throw AppException.Validation($"Product '{product.Sku}' is batch-tracked: select a batch to transfer.",
                        new Dictionary<string, string[]> { ["batchId"] = ["Batch required."] });
            }

            var number = await _numbers.NextAsync("TRANSFER", ct);
            var transfer = new StockTransfer
            {
                TenantId = TenantId, Number = number,
                SourceWarehouseId = req.SourceWarehouseId, DestinationWarehouseId = req.DestinationWarehouseId,
                Notes = req.Notes, CreatedBy = uid
            };
            transfer.Lines.AddRange(req.Lines.Select(l => new StockTransferLine
            {
                ProductId = l.ProductId, BatchId = l.BatchId, Quantity = l.Quantity
            }));
            _db.StockTransfers.Add(transfer);
            await _db.SaveChangesAsync(ct);
            await _audit.WriteAsync("Transfer.Created", "StockTransfer", transfer.Id, new { number }, ct);
            await tx.CommitAsync(ct);
            return new SimpleResult(transfer.Id, number, transfer.Status.ToString());
        }, ct);
    }

    /// <summary>Dispatch moves stock out of the source warehouse; it is in transit until received.</summary>
    public async Task<SimpleResult> DispatchAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryTransfer, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var t = await _db.StockTransfers.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Transfer");
        if (t.Status != TransferStatus.Draft)
            throw AppException.Conflict($"Only draft transfers can be dispatched (current status: {t.Status}).");

        foreach (var line in t.Lines)
        {
            var product = await _db.Products.AsNoTracking().FirstAsync(p => p.Id == line.ProductId, ct);
            var batchRef = line.BatchId;
            string? batchNumber = null;
            if (batchRef is { } bid)
            {
                var b = await _db.Batches.AsNoTracking().FirstAsync(x => x.Id == bid, ct);
                batchNumber = b.BatchNumber;
            }
            await _posting.PostAsync(new PostMovementCommand(
                line.ProductId, t.SourceWarehouseId, -line.Quantity, MovementType.TransferOut,
                null, batchRef, batchNumber, null, "StockTransfer", t.Id, t.Number, $"Transfer {t.Number} dispatched",
                false, null), ct);
        }

        t.Status = TransferStatus.InTransit;
        t.DispatchedBy = uid;
        t.DispatchedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Transfer.Dispatched", "StockTransfer", t.Id, new { t.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(t.Id, t.Number, t.Status.ToString());
    }

    public async Task<SimpleResult> ReceiveAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryTransfer, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var t = await _db.StockTransfers.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Transfer");
        if (t.Status != TransferStatus.InTransit)
            throw AppException.Conflict($"Only in-transit transfers can be received (current status: {t.Status}).");

        foreach (var line in t.Lines)
        {
            string? batchNumber = null;
            DateTime? expiry = null;
            if (line.BatchId is { } bid)
            {
                var b = await _db.Batches.AsNoTracking().FirstAsync(x => x.Id == bid, ct);
                batchNumber = b.BatchNumber;
                expiry = b.ExpiryDate;
            }
            // Re-create/reuse the batch at the destination with the same identity (lot + expiry preserved).
            await _posting.PostAsync(new PostMovementCommand(
                line.ProductId, t.DestinationWarehouseId, line.Quantity, MovementType.TransferIn,
                null, null, batchNumber, expiry, "StockTransfer", t.Id, t.Number, $"Transfer {t.Number} received",
                false, null), ct);
        }

        t.Status = TransferStatus.Received;
        t.ReceivedBy = uid;
        t.ReceivedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Transfer.Received", "StockTransfer", t.Id, new { t.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(t.Id, t.Number, t.Status.ToString());
    }

    public async Task<SimpleResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryTransfer, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var t = await _db.StockTransfers.Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Transfer");
        if (t.Status is TransferStatus.Received or TransferStatus.Cancelled)
            throw AppException.Conflict($"A {t.Status} transfer cannot be cancelled.");

        // If stock was already dispatched (in transit), return it to the source warehouse.
        if (t.Status == TransferStatus.InTransit)
        {
            foreach (var line in t.Lines)
            {
                string? batchNumber = null;
                DateTime? expiry = null;
                if (line.BatchId is { } bid)
                {
                    var b = await _db.Batches.AsNoTracking().FirstAsync(x => x.Id == bid, ct);
                    batchNumber = b.BatchNumber;
                    expiry = b.ExpiryDate;
                }
                await _posting.PostAsync(new PostMovementCommand(
                    line.ProductId, t.SourceWarehouseId, line.Quantity, MovementType.Reversal,
                    null, null, batchNumber, expiry, "StockTransfer", t.Id, t.Number,
                    $"Cancellation of transfer {t.Number}: stock returned to source",
                    false, null), ct);
            }
        }

        t.Status = TransferStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Transfer.Cancelled", "StockTransfer", t.Id, new { t.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(t.Id, t.Number, t.Status.ToString());
    }

    private async Task EnsureWarehouseAsync(Guid id, CancellationToken ct)
    {
        if (!await _db.Warehouses.AnyAsync(w => w.Id == id && w.TenantId == TenantId && w.IsActive, ct))
            throw AppException.Validation("Warehouse not found or inactive.",
                new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown or inactive warehouse."] });
    }

    private async Task<TransferDto> ToDtoAsync(StockTransfer t, CancellationToken ct)
    {
        var names = await _db.Warehouses.AsNoTracking().Where(w => w.TenantId == TenantId)
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var productIds = t.Lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var units = await _db.Units.AsNoTracking().ToListAsync(ct);
        var batchIds = t.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).ToList();
        var batches = batchIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.BatchNumber, ct);

        var lines = t.Lines.Select(l =>
        {
            var p = products.First(x => x.Id == l.ProductId);
            var unit = units.FirstOrDefault(u => u.Id == p.UnitId);
            return new TransferLineDto(p.Id, p.Sku, p.Name, unit?.Symbol ?? "?",
                l.BatchId, l.BatchId is { } bid ? batches.GetValueOrDefault(bid) : null, l.Quantity);
        }).ToList();

        return new TransferDto(t.Id, t.Number, t.SourceWarehouseId, names.GetValueOrDefault(t.SourceWarehouseId, "?"),
            t.DestinationWarehouseId, names.GetValueOrDefault(t.DestinationWarehouseId, "?"),
            t.Status.ToString(), t.Notes, t.CreatedAt, t.DispatchedAt, t.ReceivedAt, lines);
    }
}

// ---------------- Stock counts ----------------

public record StockCountDto(
    Guid Id, string Number, Guid WarehouseId, string WarehouseName, string Status, bool BlindCount,
    string? Notes, DateTime CreatedAt, DateTime? SubmittedAt, DateTime? ApprovedAt, DateTime? PostedAt,
    IReadOnlyList<StockCountLineDto> Lines);

public record StockCountLineDto(
    Guid ProductId, string Sku, string ProductName, string UnitSymbol, Guid? BatchId, string? BatchNumber,
    decimal? ExpectedQuantity, decimal? CountedQuantity, decimal? Variance, string? Reason);

public record CreateStockCountRequest(Guid WarehouseId, bool BlindCount, string? Notes,
    IReadOnlyList<CountSelection> ProductIds);
public record CountSelection(Guid ProductId);
public record SubmitCountLinesRequest(IReadOnlyList<SubmitCountLine> Lines);
public record SubmitCountLine(Guid ProductId, Guid? BatchId, decimal CountedQuantity, string? Reason);

public class CreateStockCountValidator : AbstractValidator<CreateStockCountRequest>
{
    public CreateStockCountValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.ProductIds).NotEmpty().WithMessage("Select at least one product to count.");
    }
}

public class StockCountService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly InventoryPostingService _posting;
    private readonly INumberGenerator _numbers;
    private readonly IAuditService _audit;

    public StockCountService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions,
        InventoryPostingService posting, INumberGenerator numbers, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions;
        _posting = posting; _numbers = numbers; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<StockCountDto>> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.InventoryRead, ct);
        var q = _db.StockCounts.AsNoTracking().Where(c => c.TenantId == TenantId)
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new StockCountDto(c.Id, c.Number, c.WarehouseId,
                _db.Warehouses.Where(w => w.Id == c.WarehouseId).Select(w => w.Name).FirstOrDefault() ?? "?",
                c.Status.ToString(), c.BlindCount, c.Notes, c.CreatedAt, c.SubmittedAt, c.ApprovedAt, c.PostedAt, new StockCountLineDto[] { }));
        return await q.ToPageResultAsync(page, pageSize, ct);
    }

    public async Task<StockCountDto> GetAsync(Guid id, CancellationToken ct)
    {
        var c = await _db.StockCounts.AsNoTracking().Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        return await ToDtoAsync(c, ct);
    }

    /// <summary>Creates a count session and snapshots expected quantities. Requires Inventory.Count.</summary>
    public async Task<SimpleResult> CreateAsync(CreateStockCountRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCount, ct);
        if (!await _db.Warehouses.AnyAsync(w => w.Id == req.WarehouseId && w.TenantId == TenantId, ct))
            throw AppException.Validation("Warehouse not found.",
                new Dictionary<string, string[]> { ["warehouseId"] = ["Unknown warehouse."] });

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var number = await _numbers.NextAsync("STOCK_COUNT", ct);
        var count = new StockCount
        {
            TenantId = TenantId, Number = number, WarehouseId = req.WarehouseId,
            BlindCount = req.BlindCount, Notes = req.Notes, CreatedBy = uid
        };

        var productIds = req.ProductIds.Select(p => p.ProductId).Distinct().ToList();
        var balances = await _db.InventoryBalances.AsNoTracking()
            .Where(b => b.TenantId == TenantId && b.WarehouseId == req.WarehouseId && productIds.Contains(b.ProductId))
            .ToDictionaryAsync(b => b.ProductId, b => b.Quantity, ct);

        foreach (var pid in productIds)
        {
            var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.TenantId == TenantId, ct)
                ?? throw AppException.NotFound($"Product {pid}");
            var expected = balances.GetValueOrDefault(pid, 0m);
            count.Lines.Add(new StockCountLine
            {
                ProductId = pid, ExpectedQuantity = expected, CountedQuantity = null
            });
        }

        _db.StockCounts.Add(count);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Created", "StockCount", count.Id, new { number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(count.Id, number, count.Status.ToString());
    }

    /// <summary>Records counted quantities (blind counts never reveal expected values to the counter).</summary>
    public async Task<SimpleResult> SubmitCountedAsync(Guid id, SubmitCountLinesRequest req, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCount, ct);
        var count = await _db.StockCounts.Include(x => x.Lines)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        if (count.Status != StockCountStatus.Draft)
            throw AppException.Conflict($"Only draft counts accept counted quantities (current status: {count.Status}).");
        if (count.BlindCount)
            throw AppException.Conflict("This is a blind count: counted values were recorded at count time. Submit it for review instead.");

        foreach (var line in req.Lines)
        {
            var l = count.Lines.FirstOrDefault(x => x.ProductId == line.ProductId && x.BatchId == line.BatchId)
                ?? throw AppException.Validation($"Product {line.ProductId} is not part of this count.");
            if (line.CountedQuantity < 0)
                throw AppException.Validation("Counted quantity cannot be negative.");
            l.CountedQuantity = line.CountedQuantity;
            l.Reason = line.Reason;
        }
        // Values are recorded only; submission is an explicit transition via POST /submit.
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Counted", "StockCount", id, new { lines = req.Lines.Count }, ct);
        return new SimpleResult(count.Id, count.Number, count.Status.ToString());
    }

    public async Task<SimpleResult> SubmitAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCount, ct);
        var count = await _db.StockCounts.Include(x => x.Lines)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        if (count.Status != StockCountStatus.Draft)
            throw AppException.Conflict($"Only draft counts can be submitted (current status: {count.Status}).");
        if (count.Lines.Any(l => l.CountedQuantity == null))
            throw AppException.Conflict("All lines must have a counted quantity before submitting.");
        count.Status = StockCountStatus.Submitted;
        count.SubmittedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Submitted", "StockCount", id, null, ct);
        return new SimpleResult(count.Id, count.Number, count.Status.ToString());
    }

    public async Task<SimpleResult> ApproveAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCountApprove, ct);
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        if (count.Status != StockCountStatus.Submitted)
            throw AppException.Conflict($"Only submitted counts can be approved (current status: {count.Status}).");
        count.Status = StockCountStatus.Approved;
        count.ApprovedBy = uid;
        count.ApprovedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Approved", "StockCount", id, null, ct);
        return new SimpleResult(count.Id, count.Number, count.Status.ToString());
    }

    /// <summary>Posts approved variances as immutable ledger movements. The count can only be posted once.</summary>
    public async Task<SimpleResult> PostAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCountApprove, ct);
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var count = await _db.StockCounts.Include(x => x.Lines)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        if (count.Status != StockCountStatus.Approved)
            throw AppException.Conflict($"Only approved counts can be posted (current status: {count.Status}).");

        foreach (var line in count.Lines.Where(l => l.CountedQuantity != null))
        {
            var variance = line.CountedQuantity!.Value - line.ExpectedQuantity;
            if (variance == 0) continue;
            await _posting.PostAsync(new PostMovementCommand(
                line.ProductId, count.WarehouseId, variance, MovementType.StockCountVariance,
                null, line.BatchId, null, null, "StockCount", count.Id, count.Number,
                line.Reason ?? $"Count {count.Number} variance",
                false, null), ct);
        }

        count.Status = StockCountStatus.Posted;
        count.PostedBy = uid;
        count.PostedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Posted", "StockCount", id, new { count.Number }, ct);
        await tx.CommitAsync(ct);
        return new SimpleResult(count.Id, count.Number, count.Status.ToString());
    }

    public async Task<SimpleResult> CancelAsync(Guid id, CancellationToken ct)
    {
        var uid = _currentUser.UserId!.Value;
        await _permissions.EnsurePermissionAsync(uid, TenantId, Permissions.InventoryCount, ct);
        var count = await _db.StockCounts.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Stock count");
        if (count.Status is StockCountStatus.Posted)
            throw AppException.Conflict("A posted count cannot be cancelled.");
        count.Status = StockCountStatus.Cancelled;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("StockCount.Cancelled", "StockCount", id, null, ct);
        return new SimpleResult(count.Id, count.Number, count.Status.ToString());
    }

    private async Task<StockCountDto> ToDtoAsync(StockCount c, CancellationToken ct)
    {
        var warehouseName = await _db.Warehouses.AsNoTracking()
            .Where(w => w.Id == c.WarehouseId).Select(w => w.Name).FirstOrDefaultAsync(ct) ?? "?";
        var productIds = c.Lines.Select(l => l.ProductId).ToList();
        var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
        var units = await _db.Units.AsNoTracking().ToListAsync(ct);
        var batchIds = c.Lines.Where(l => l.BatchId != null).Select(l => l.BatchId!.Value).ToList();
        var batches = batchIds.Count == 0 ? new Dictionary<Guid, string>()
            : await _db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id, b => b.BatchNumber, ct);

        var lines = c.Lines.Select(l =>
        {
            var p = products.First(x => x.Id == l.ProductId);
            var unit = units.FirstOrDefault(u => u.Id == p.UnitId);
            decimal? variance = l.CountedQuantity is { } counted ? counted - l.ExpectedQuantity : null;
            return new StockCountLineDto(p.Id, p.Sku, p.Name, unit?.Symbol ?? "?", l.BatchId,
                l.BatchId is { } bid ? batches.GetValueOrDefault(bid) : null,
                c.BlindCount && c.Status != StockCountStatus.Posted ? null : l.ExpectedQuantity,
                l.CountedQuantity, variance, l.Reason);
        }).ToList();

        return new StockCountDto(c.Id, c.Number, c.WarehouseId, warehouseName, c.Status.ToString(),
            c.BlindCount, c.Notes, c.CreatedAt, c.SubmittedAt, c.ApprovedAt, c.PostedAt, lines);
    }
}
