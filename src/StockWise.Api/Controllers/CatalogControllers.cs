using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using StockWise.Api.Infrastructure;
using StockWise.Application.Common;
using StockWise.Application.Features.Catalog;
using StockWise.Application.Features.Insights;
using StockWise.Application.Features.Products;
using StockWise.Domain.Exceptions;

namespace StockWise.Api.Controllers;

// ---------------- Categories / Units / Warehouses ----------------

[ApiController]
[Route("api/v1/categories")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly CatalogService _catalog;

    public CategoriesController(CatalogService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _catalog.ListCategoriesAsync(includeInactive, ct));

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> Create(CreateCategoryRequest req, CancellationToken ct)
        => Ok(await _catalog.CreateCategoryAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CategoryDto>> Update(Guid id, UpdateCategoryRequest req, CancellationToken ct)
        => Ok(await _catalog.UpdateCategoryAsync(id, req, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await _catalog.ArchiveCategoryAsync(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/units")]
[Authorize]
public class UnitsController : ControllerBase
{
    private readonly CatalogService _catalog;

    public UnitsController(CatalogService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UnitDto>>> List(CancellationToken ct)
        => Ok(await _catalog.ListUnitsAsync(ct));

    [HttpPost]
    public async Task<ActionResult<UnitDto>> Create(CreateUnitRequest req, CancellationToken ct)
        => Ok(await _catalog.CreateUnitAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UnitDto>> Update(Guid id, UpdateUnitRequest req, CancellationToken ct)
        => Ok(await _catalog.UpdateUnitAsync(id, req, ct));
}

[ApiController]
[Route("api/v1/warehouses")]
[Authorize]
public class WarehousesController : ControllerBase
{
    private readonly CatalogService _catalog;

    public WarehousesController(CatalogService catalog) => _catalog = catalog;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> List([FromQuery] bool includeInactive = false, CancellationToken ct = default)
        => Ok(await _catalog.ListWarehousesAsync(includeInactive, ct));

    [HttpPost]
    public async Task<ActionResult<WarehouseDto>> Create(CreateWarehouseRequest req, CancellationToken ct)
        => Ok(await _catalog.CreateWarehouseAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<WarehouseDto>> Update(Guid id, UpdateWarehouseRequest req, CancellationToken ct)
        => Ok(await _catalog.UpdateWarehouseAsync(id, req, ct));
}

// ---------------- Suppliers ----------------

[ApiController]
[Route("api/v1/suppliers")]
[Authorize]
public class SuppliersController : ControllerBase
{
    private readonly SupplierService _suppliers;

    public SuppliersController(SupplierService suppliers) => _suppliers = suppliers;

    [HttpGet]
    public async Task<ActionResult<PageResult<SupplierDto>>> List(
        [FromQuery] string? search, [FromQuery] bool? active,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await _suppliers.ListAsync(search, active, page, pageSize, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _suppliers.GetAsync(id, ct));

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(SaveSupplierRequest req, CancellationToken ct)
        => Ok(await _suppliers.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<SupplierDto>> Update(Guid id, SaveSupplierRequest req, CancellationToken ct)
        => Ok(await _suppliers.UpdateAsync(id, req, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await _suppliers.ArchiveAsync(id, ct);
        return NoContent();
    }
}

// ---------------- Products ----------------

[ApiController]
[Route("api/v1/products")]
[Authorize]
public class ProductsController : ControllerBase
{
    private readonly ProductService _products;

    public ProductsController(ProductService products) => _products = products;

    [HttpGet]
    public async Task<ActionResult<PageResult<ProductDto>>> List([FromQuery] ProductListQuery query, CancellationToken ct)
        => Ok(await _products.ListAsync(query, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _products.GetAsync(id, ct));

    [HttpGet("{id:guid}/movements")]
    public async Task<ActionResult<IReadOnlyList<ProductMovementDto>>> GetMovements(
        Guid id, [FromQuery] int limit = 50, CancellationToken ct = default)
        => Ok(await _products.GetMovementsAsync(id, limit, ct));

    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(SaveProductRequest req, CancellationToken ct)
        => Ok(await _products.CreateAsync(req, ct));

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, SaveProductRequest req, CancellationToken ct)
        => Ok(await _products.UpdateAsync(id, req, ct));

    [HttpPost("{id:guid}/archive")]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        await _products.ArchiveAsync(id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        await _products.RestoreAsync(id, ct);
        return NoContent();
    }

    /// <summary>CSV import step 1: validate and preview rows without committing.</summary>
    [HttpPost("import/preview")]
    public async Task<ActionResult<ProductService.ImportPreview>> ImportPreview(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw AppException.BadRequest("Attach a CSV file in the 'file' form field.");
        if (file.Length > 5 * 1024 * 1024)
            throw AppException.BadRequest("File too large (max 5 MB).");
        if (!file.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
            throw AppException.BadRequest("Only .csv files are supported.");
        await using var stream = file.OpenReadStream();
        return Ok(await _products.PreviewImportAsync(stream, ct));
    }

    /// <summary>CSV import step 2: commit valid rows. Any duplicate SKU aborts the whole import.</summary>
    [HttpPost("import/commit")]
    public async Task<ActionResult<ProductService.ImportCommitResult>> ImportCommit(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            throw AppException.BadRequest("Attach a CSV file in the 'file' form field.");
        if (file.Length > 5 * 1024 * 1024)
            throw AppException.BadRequest("File too large (max 5 MB).");
        await using var stream = file.OpenReadStream();
        return Ok(await _products.CommitImportAsync(stream, ct));
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] ProductListQuery query, CancellationToken ct)
    {
        var (content, fileName) = await _products.ExportCsvAsync(query, ct);
        return File(content, "text/csv; charset=utf-8", fileName);
    }
}

// ---------------- Dashboard ----------------

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly DashboardService _dashboard;
    private readonly NotificationService _notifications;

    public DashboardController(DashboardService dashboard, NotificationService notifications)
    {
        _dashboard = dashboard;
        _notifications = notifications;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken ct)
        => Ok(await _dashboard.GetSummaryAsync(ct));

    [HttpGet("top-products")]
    public async Task<ActionResult<IReadOnlyList<TopProductDto>>> TopProducts(
        [FromQuery] DateTime? from, [FromQuery] DateTime? to, [FromQuery] int take = 10, CancellationToken ct = default)
    {
        var fromUtc = from ?? DateTime.UtcNow.AddDays(-30);
        var toUtc = to ?? DateTime.UtcNow;
        return Ok(await _dashboard.GetTopProductsAsync(fromUtc, toUtc, take, ct));
    }

    [HttpGet("trends")]
    public async Task<ActionResult<IReadOnlyList<TrendPointDto>>> Trends(
        [FromQuery] int days = 30, CancellationToken ct = default)
        => Ok(await _dashboard.GetTrendsAsync(days, ct));

    [HttpGet("alerts")]
    public async Task<ActionResult<IReadOnlyList<NotificationDto>>> Alerts(CancellationToken ct)
    {
        await _notifications.SweepAsync(ct);
        return Ok(await _notifications.ListAsync(unreadOnly: false, limit: 25, ct));
    }
}
