using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockWise.Api.Infrastructure;
using StockWise.Application.Common;
using StockWise.Application.Features.Insights;
using StockWise.Application.Features.Inventory;
using StockWise.Domain.Exceptions;
using StockWise.Application.Features.Procurement;

namespace StockWise.Api.Controllers;

[ApiController]
[Route("api/v1/purchase-orders")]
[Authorize]
public class PurchaseOrdersController : ControllerBase
{
    private readonly PurchaseOrderService _orders;
    private readonly GoodsReceiptService _receipts;

    public PurchaseOrdersController(PurchaseOrderService orders, GoodsReceiptService receipts)
    {
        _orders = orders;
        _receipts = receipts;
    }

    [HttpGet]
    public async Task<ActionResult<PageResult<PurchaseOrderDto>>> List(
        [FromQuery] string? status, [FromQuery] Guid? supplierId, [FromQuery] string? search,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _orders.ListAsync(status, supplierId, search, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _orders.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SimpleResult>> Create(SavePurchaseOrderRequest req, CancellationToken ct)
        => Ok(await _orders.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SimpleResult>> Update(Guid id, SavePurchaseOrderRequest req, CancellationToken ct)
        => Ok(await _orders.UpdateAsync(id, req, ct));

    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult<SimpleResult>> Submit(Guid id, CancellationToken ct)
        => Ok(await _orders.SubmitAsync(id, ct));

    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult<SimpleResult>> Approve(Guid id, CancellationToken ct)
        => Ok(await _orders.ApproveAsync(id, ct));

    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult<SimpleResult>> Reject(Guid id, RejectRequest req, CancellationToken ct)
        => Ok(await _orders.RejectAsync(id, req, ct));

    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult<SimpleResult>> Cancel(Guid id, CancellationToken ct)
        => Ok(await _orders.CancelAsync(id, ct));

    [HttpPost("{id:guid}/order")]
    public async Task<ActionResult<SimpleResult>> MarkOrdered(Guid id, CancellationToken ct)
        => Ok(await _orders.MarkOrderedAsync(id, ct));

    /// <summary>
    /// Posts a goods receipt against this order. Stock changes atomically; duplicates are
    /// rejected via the idempotency key.
    /// </summary>
    [HttpPost("{id:guid}/receipts")]
    public async Task<ActionResult<ReceiptResult>> Receive(Guid id, CreateReceiptRequest req, CancellationToken ct)
    {
        if (req.PurchaseOrderId == Guid.Empty) req = req with { PurchaseOrderId = id };
        if (req.PurchaseOrderId != id)
            throw new AppException("Purchase order id mismatch.", 400, "bad_request");
        return Ok(await _receipts.CreateAsync(req, ct));
    }
}

[ApiController]
[Route("api/v1/goods-receipts")]
[Authorize]
public class GoodsReceiptsController : ControllerBase
{
    private readonly GoodsReceiptService _receipts;

    public GoodsReceiptsController(GoodsReceiptService receipts) => _receipts = receipts;

    [HttpGet]
    public async Task<ActionResult<PageResult<GoodsReceiptDto>>> List(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _receipts.ListAsync(page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<GoodsReceiptDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _receipts.GetAsync(id, ct));
}

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly NotificationService _notifications;

    public NotificationsController(NotificationService notifications) => _notifications = notifications;

    /// <summary>Lists notifications; runs a dedup sweep so alerts stay current.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> List(
        [FromQuery] bool? unreadOnly, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        await _notifications.SweepAsync(ct);
        return Ok(await _notifications.ListAsync(unreadOnly, limit, ct));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken ct)
        => Ok(await _notifications.UnreadCountAsync(ct));

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        await _notifications.MarkReadAsync(id, ct);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken ct)
    {
        await _notifications.MarkAllReadAsync(ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reports;

    public ReportsController(ReportService reports) => _reports = reports;

    /// <summary>Server-side aggregates powering the reports dashboard charts.</summary>
    [HttpGet("charts")]
    public async Task<ActionResult<ReportChartsDto>> Charts(
        [FromQuery] DateTime? fromUtc, [FromQuery] DateTime? toUtc, CancellationToken ct)
        => Ok(await _reports.GetChartsAsync(fromUtc, toUtc, ct));

    [HttpGet("stock-on-hand")]
    public async Task<IActionResult> StockOnHand([FromQuery] ReportRequest req, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await _reports.StockOnHandAsync(req, ct);
        return format == "csv"
            ? File(ReportService.ToCsv(report.Items), "text/csv; charset=utf-8", "stock-on-hand.csv")
            : Ok(report);
    }

    [HttpGet("inventory-valuation")]
    public async Task<IActionResult> Valuation([FromQuery] ReportRequest req, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await _reports.InventoryValuationAsync(req, ct);
        return format == "csv"
            ? File(ReportService.ToCsv(report.Items), "text/csv; charset=utf-8", "inventory-valuation.csv")
            : Ok(report);
    }

    [HttpGet("movements")]
    public async Task<IActionResult> Movements([FromQuery] ReportRequest req, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await _reports.MovementsAsync(req, ct);
        return format == "csv"
            ? File(ReportService.ToCsv(report.Items), "text/csv; charset=utf-8", "movements.csv")
            : Ok(report);
    }

    [HttpGet("purchases")]
    public async Task<IActionResult> Purchases([FromQuery] ReportRequest req, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await _reports.PurchasesAsync(req, ct);
        return format == "csv"
            ? File(ReportService.ToCsv(report.Items), "text/csv; charset=utf-8", "purchases.csv")
            : Ok(report);
    }

    [HttpGet("adjustments")]
    public async Task<IActionResult> Adjustments([FromQuery] ReportRequest req, [FromQuery] string? format, CancellationToken ct)
    {
        var report = await _reports.AdjustmentsAsync(req, ct);
        return format == "csv"
            ? File(ReportService.ToCsv(report.Items), "text/csv; charset=utf-8", "adjustments.csv")
            : Ok(report);
    }
}
