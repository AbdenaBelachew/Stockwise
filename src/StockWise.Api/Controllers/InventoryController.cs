using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockWise.Api.Infrastructure;
using StockWise.Application.Common;
using StockWise.Application.Features.Inventory;

namespace StockWise.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventory;
    private readonly TransferService _transfers;
    private readonly StockCountService _counts;

    public InventoryController(InventoryService inventory, TransferService transfers, StockCountService counts)
    {
        _inventory = inventory;
        _transfers = transfers;
        _counts = counts;
    }

    /// <summary>Stock on hand per product and warehouse (concurrency-protected balance projection).</summary>
    [HttpGet("balances")]
    public async Task<ActionResult<PageResult<BalanceDto>>> Balances(
        [FromQuery] Guid? productId, [FromQuery] Guid? warehouseId, [FromQuery] string? stockStatus,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
        => Ok(await _inventory.GetBalancesAsync(productId, warehouseId, stockStatus, search, page, pageSize, ct));

    /// <summary>The immutable movement ledger, newest first.</summary>
    [HttpGet("movements")]
    public async Task<ActionResult<PageResult<MovementDto>>> Movements([FromQuery] MovementListQuery query, CancellationToken ct)
        => Ok(await _inventory.GetMovementsAsync(query, ct));

    /// <summary>Posts signed stock adjustments (positive or negative) with an audit reason per line.</summary>
    [HttpPost("adjustments")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryAdjust)]
    public async Task<ActionResult<AdjustmentResult>> Adjustments(CreateAdjustmentRequest req, CancellationToken ct)
        => Ok(await _inventory.CreateAdjustmentAsync(req, ct));

    /// <summary>Records opening balances through auditable ledger movements.</summary>
    [HttpPost("opening-balances")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryAdjust)]
    public async Task<ActionResult<AdjustmentResult>> OpeningBalances(CreateOpeningBalanceRequest req, CancellationToken ct)
        => Ok(await _inventory.CreateOpeningBalanceAsync(req, ct));

    [HttpGet("low-stock")]
    public async Task<ActionResult<IReadOnlyList<LowStockItemDto>>> LowStock(
        [FromQuery] Guid? warehouseId, [FromQuery] bool includeOutOfStock = true, CancellationToken ct = default)
        => Ok(await _inventory.GetLowStockAsync(warehouseId, includeOutOfStock, ct));

    /// <summary>Batches grouped by expiry bucket (expired / critical / warning / ok).</summary>
    [HttpGet("expiry")]
    public async Task<ActionResult<IReadOnlyList<ExpiryBatchDto>>> Expiry(
        [FromQuery] Guid? warehouseId, [FromQuery] int? daysAhead, [FromQuery] bool includeExpired = true, CancellationToken ct = default)
        => Ok(await _inventory.GetExpiryAsync(warehouseId, daysAhead, includeExpired, ct));

    [HttpPost("batches/{id:guid}/quarantine")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.BatchesManage)]
    public async Task<IActionResult> Quarantine(Guid id, [FromBody] NoteRequest? req, CancellationToken ct)
    {
        await _inventory.QuarantineBatchAsync(id, req?.Note, ct);
        return NoContent();
    }

    [HttpPost("batches/{id:guid}/release")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.BatchesManage)]
    public async Task<IActionResult> Release(Guid id, [FromBody] NoteRequest? req, CancellationToken ct)
    {
        await _inventory.ReleaseBatchAsync(id, req?.Note, ct);
        return NoContent();
    }

    public record NoteRequest(string? Note);

    // ---------------- Transfers ----------------

    [HttpGet("transfers")]
    public async Task<ActionResult<PageResult<TransferDto>>> Transfers(
        [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _transfers.ListAsync(status, page, pageSize, ct));

    [HttpGet("transfers/{id:guid}")]
    public async Task<ActionResult<TransferDto>> Transfer(Guid id, CancellationToken ct)
        => Ok(await _transfers.GetAsync(id, ct));

    [HttpPost("transfers")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryTransfer)]
    public async Task<ActionResult<SimpleResult>> CreateTransfer(CreateTransferRequest req, CancellationToken ct)
        => Ok(await _transfers.CreateAsync(req, ct));

    [HttpPost("transfers/{id:guid}/dispatch")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryTransfer)]
    public async Task<ActionResult<SimpleResult>> DispatchTransfer(Guid id, CancellationToken ct)
        => Ok(await _transfers.DispatchAsync(id, ct));

    [HttpPost("transfers/{id:guid}/receive")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryTransfer)]
    public async Task<ActionResult<SimpleResult>> ReceiveTransfer(Guid id, CancellationToken ct)
        => Ok(await _transfers.ReceiveAsync(id, ct));

    [HttpPost("transfers/{id:guid}/cancel")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryTransfer)]
    public async Task<ActionResult<SimpleResult>> CancelTransfer(Guid id, CancellationToken ct)
        => Ok(await _transfers.CancelAsync(id, ct));

    // ---------------- Stock counts ----------------

    [HttpGet("stock-counts")]
    public async Task<ActionResult<PageResult<StockCountDto>>> StockCounts(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _counts.ListAsync(page, pageSize, ct));

    [HttpGet("stock-counts/{id:guid}")]
    public async Task<ActionResult<StockCountDto>> StockCount(Guid id, CancellationToken ct)
        => Ok(await _counts.GetAsync(id, ct));

    [HttpPost("stock-counts")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCount)]
    public async Task<ActionResult<SimpleResult>> CreateCount(CreateStockCountRequest req, CancellationToken ct)
        => Ok(await _counts.CreateAsync(req, ct));

    [HttpPost("stock-counts/{id:guid}/count")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCount)]
    public async Task<ActionResult<SimpleResult>> SubmitCounted(Guid id, SubmitCountLinesRequest req, CancellationToken ct)
        => Ok(await _counts.SubmitCountedAsync(id, req, ct));

    [HttpPost("stock-counts/{id:guid}/submit")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCount)]
    public async Task<ActionResult<SimpleResult>> SubmitCount(Guid id, CancellationToken ct)
        => Ok(await _counts.SubmitAsync(id, ct));

    [HttpPost("stock-counts/{id:guid}/approve")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCountApprove)]
    public async Task<ActionResult<SimpleResult>> ApproveCount(Guid id, CancellationToken ct)
        => Ok(await _counts.ApproveAsync(id, ct));

    [HttpPost("stock-counts/{id:guid}/post")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCountApprove)]
    public async Task<ActionResult<SimpleResult>> PostCount(Guid id, CancellationToken ct)
        => Ok(await _counts.PostAsync(id, ct));

    [HttpPost("stock-counts/{id:guid}/cancel")]
    [AuthorizePermission(StockWise.Application.Common.Permissions.InventoryCount)]
    public async Task<ActionResult<SimpleResult>> CancelCount(Guid id, CancellationToken ct)
        => Ok(await _counts.CancelAsync(id, ct));
}
