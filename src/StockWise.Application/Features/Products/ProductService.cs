using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Products;

public record ProductDto(
    Guid Id, string Sku, string? Barcode, string Name, string? Description, string? ImageUrl,
    Guid? CategoryId, string? CategoryName, string? Brand, Guid UnitId, string UnitName, string UnitSymbol,
    decimal? CostPrice, decimal? SalePrice, string CurrencyCode, string CostMethod,
    decimal ReorderPoint, decimal? TargetStockLevel, Guid? DefaultSupplierId, string? DefaultSupplierName,
    bool TrackBatches, bool TrackExpiry, int? MinShelfLifeDays, string Status,
    decimal OnHandTotal, DateTime CreatedAt);

public record ProductListQuery(
    string? Search, Guid? CategoryId, string? Status, Guid? SupplierId,
    string? StockStatus, Guid? WarehouseId, string? Sort, int Page = 1, int PageSize = 25);

public record SaveProductRequest(
    string Sku, string? Barcode, string Name, string? Description, string? ImageUrl,
    Guid? CategoryId, string? Brand, Guid UnitId, decimal? CostPrice, decimal? SalePrice,
    decimal ReorderPoint, decimal? TargetStockLevel, Guid? DefaultSupplierId,
    bool TrackBatches, bool TrackExpiry, int? MinShelfLifeDays);

public record ProductMovementDto(
    Guid Id, string MovementType, decimal QuantityDelta, decimal? UnitCost, string? ReferenceType,
    string? ReferenceNumber, string? Reason, string WarehouseName, string? BatchNumber, string UserName, DateTime CreatedAtUtc);

public class SaveProductValidator : AbstractValidator<SaveProductRequest>
{
    public SaveProductValidator()
    {
        RuleFor(x => x.Sku).NotEmpty().MaximumLength(80);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(300);
        RuleFor(x => x.UnitId).NotEmpty();
        RuleFor(x => x.CostPrice).GreaterThanOrEqualTo(0).When(x => x.CostPrice.HasValue);
        RuleFor(x => x.SalePrice).GreaterThanOrEqualTo(0).When(x => x.SalePrice.HasValue);
        RuleFor(x => x.ReorderPoint).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TargetStockLevel).GreaterThanOrEqualTo(0).When(x => x.TargetStockLevel.HasValue);
        RuleFor(x => x.MinShelfLifeDays).GreaterThanOrEqualTo(0).When(x => x.MinShelfLifeDays.HasValue);
        RuleFor(x => x.TrackExpiry).Equal(false).When(x => !x.TrackBatches)
            .WithMessage("Expiry tracking requires batch tracking to be enabled.");
    }
}

public class ProductService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly IAuditService _audit;

    public ProductService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<ProductDto>> ListAsync(ProductListQuery q, CancellationToken ct)
    {
        var query = _db.Products.AsNoTracking()
            .Where(p => p.TenantId == TenantId)
            .Select(p => new
            {
                p,
                UnitName = _db.Units.Where(u => u.Id == p.UnitId).Select(u => u.Name).FirstOrDefault(),
                UnitSymbol = _db.Units.Where(u => u.Id == p.UnitId).Select(u => u.Symbol).FirstOrDefault(),
                CategoryName = _db.Categories.Where(c => c.Id == p.CategoryId).Select(c => c.Name).FirstOrDefault(),
                SupplierName = _db.Suppliers.Where(s => s.Id == p.DefaultSupplierId).Select(s => s.Name).FirstOrDefault(),
                OnHand = _db.InventoryBalances.Where(b => b.ProductId == p.Id).Sum(b => (decimal?)b.Quantity) ?? 0m
            });

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var term = q.Search.Trim().ToLower();
            query = query.Where(x => x.p.Name.ToLower().Contains(term)
                || x.p.Sku.ToLower().Contains(term)
                || (x.p.Barcode != null && x.p.Barcode.ToLower().Contains(term)));
        }
        if (q.CategoryId is { } cat) query = query.Where(x => x.p.CategoryId == cat);
        if (q.SupplierId is { } sup) query = query.Where(x => x.p.DefaultSupplierId == sup);
        if (q.Status is { } st && Enum.TryParse<ProductStatus>(st, true, out var ps))
            query = query.Where(x => x.p.Status == ps);
        if (q.StockStatus is { } stock)
        {
            query = stock.ToLowerInvariant() switch
            {
                "out" => query.Where(x => x.OnHand == 0),
                "low" => query.Where(x => x.OnHand > 0 && x.OnHand <= x.p.ReorderPoint),
                "in" => query.Where(x => x.OnHand > x.p.ReorderPoint),
                _ => query
            };
        }

        query = (q.Sort?.ToLowerInvariant()) switch
        {
            "name_desc" => query.OrderByDescending(x => x.p.Name),
            "sku" => query.OrderBy(x => x.p.Sku),
            "sku_desc" => query.OrderByDescending(x => x.p.Sku),
            "onhand" => query.OrderBy(x => x.OnHand),
            "onhand_desc" => query.OrderByDescending(x => x.OnHand),
            _ => query.OrderBy(x => x.p.Name)
        };

        var page = await query.ToPageResultAsync(q.Page, q.PageSize, ct);
        var currency = (await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct)).CurrencyCode;

        var items = page.Items.Select(x => new ProductDto(
            x.p.Id, x.p.Sku, x.p.Barcode, x.p.Name, x.p.Description, x.p.ImageUrl,
            x.p.CategoryId, x.CategoryName, x.p.Brand, x.p.UnitId, x.UnitName ?? "?", x.UnitSymbol ?? "?",
            x.p.CostPrice, x.p.SalePrice, currency, x.p.CostMethod.ToString(),
            x.p.ReorderPoint, x.p.TargetStockLevel, x.p.DefaultSupplierId, x.SupplierName,
            x.p.TrackBatches, x.p.TrackExpiry, x.p.MinShelfLifeDays, x.p.Status.ToString(),
            x.OnHand, x.p.CreatedAt)).ToList();

        return new PageResult<ProductDto> { Items = items, Page = page.Page, PageSize = page.PageSize, TotalCount = page.TotalCount };
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct)
    {
        var p = await _db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Product");
        var unit = await _db.Units.AsNoTracking().FirstAsync(u => u.Id == p.UnitId, ct);
        var cat = p.CategoryId is { } cid
            ? await _db.Categories.AsNoTracking().Where(c => c.Id == cid).Select(c => c.Name).FirstOrDefaultAsync(ct)
            : null;
        var supplier = p.DefaultSupplierId is { } sid
            ? await _db.Suppliers.AsNoTracking().Where(s => s.Id == sid).Select(s => s.Name).FirstOrDefaultAsync(ct)
            : null;
        var onHand = await _db.InventoryBalances.AsNoTracking()
            .Where(b => b.ProductId == id).SumAsync(b => (decimal?)b.Quantity, ct) ?? 0m;
        var org = await _db.Organizations.AsNoTracking().FirstAsync(o => o.Id == TenantId, ct);
        return ToDto(p, unit, cat, supplier, onHand, org.CurrencyCode);
    }

    public async Task<ProductDto> CreateAsync(SaveProductRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsCreate, ct);
        await ValidateReferencesAsync(req, ct);
        await EnsureUniqueAsync(req.Sku, req.Barcode, null, ct);

        var p = MapToEntity(req);
        p.TenantId = TenantId;
        p.CreatedBy = _currentUser.UserId;
        _db.Products.Add(p);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Product.Created", "Product", p.Id, new { p.Sku, p.Name }, ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, SaveProductRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsUpdate, ct);
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Product");
        await ValidateReferencesAsync(req, ct);
        await EnsureUniqueAsync(req.Sku, req.Barcode, id, ct);

        // Structural/identity fields of products with transactions stay guarded: SKU changes are allowed
        // but batch/expiry toggles are locked once batches or movements exist.
        var hasBatches = await _db.Batches.AsNoTracking().AnyAsync(b => b.ProductId == id, ct);
        var hasMovements = await _db.InventoryMovements.AsNoTracking().AnyAsync(m => m.ProductId == id, ct);
        if ((hasBatches || hasMovements) && (req.TrackBatches != p.TrackBatches || req.TrackExpiry != p.TrackExpiry))
            throw AppException.Conflict("Batch/expiry tracking cannot be changed for a product with inventory history.");

        p.Sku = req.Sku.Trim().ToUpperInvariant();
        p.Barcode = string.IsNullOrWhiteSpace(req.Barcode) ? null : req.Barcode.Trim();
        p.Name = req.Name.Trim();
        p.Description = req.Description;
        p.ImageUrl = req.ImageUrl;
        p.CategoryId = req.CategoryId;
        p.Brand = req.Brand;
        p.UnitId = req.UnitId;
        p.CostPrice = req.CostPrice;
        p.SalePrice = req.SalePrice;
        p.ReorderPoint = req.ReorderPoint;
        p.TargetStockLevel = req.TargetStockLevel;
        p.DefaultSupplierId = req.DefaultSupplierId;
        p.TrackBatches = req.TrackBatches;
        p.TrackExpiry = req.TrackExpiry;
        p.MinShelfLifeDays = req.MinShelfLifeDays;
        p.UpdatedBy = _currentUser.UserId;
        p.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Product.Updated", "Product", id, new { p.Sku }, ct);
        return await GetAsync(id, ct);
    }

    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsArchive, ct);
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Product");
        p.Status = ProductStatus.Archived;
        p.UpdatedBy = _currentUser.UserId;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Product.Archived", "Product", id, null, ct);
    }

    public async Task RestoreAsync(Guid id, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsArchive, ct);
        var p = await _db.Products.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Product");
        p.Status = ProductStatus.Active;
        p.UpdatedBy = _currentUser.UserId;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Product.Restored", "Product", id, null, ct);
    }

    public async Task<IReadOnlyList<ProductMovementDto>> GetMovementsAsync(Guid productId, int limit, CancellationToken ct)
    {
        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.TenantId == TenantId && m.ProductId == productId)
            .OrderByDescending(m => m.CreatedAtUtc).Take(Math.Clamp(limit, 1, 500))
            .ToListAsync(ct);
        var warehouseIds = movements.Select(m => m.WarehouseId).Distinct().ToList();
        var warehouses = await _db.Warehouses.AsNoTracking().Where(w => warehouseIds.Contains(w.Id))
            .ToDictionaryAsync(w => w.Id, w => w.Name, ct);
        var users = await _db.Users.AsNoTracking().Where(u => movements.Select(m => m.UserId).Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);
        var batchIds = movements.Where(m => m.BatchId != null).Select(m => m.BatchId!.Value).ToList();
        var batches = batchIds.Count == 0
            ? []
            : await _db.Batches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, b => b.BatchNumber, ct);

        return movements.Select(m => new ProductMovementDto(
            m.Id, m.MovementType.ToString(), m.QuantityDelta, m.UnitCost, m.ReferenceType,
            m.ReferenceNumber, m.Reason,
            warehouses.GetValueOrDefault(m.WarehouseId, "?"),
            m.BatchId is { } bid ? batches.GetValueOrDefault(bid) : null,
            users.GetValueOrDefault(m.UserId, "system"), m.CreatedAtUtc)).ToList();
    }

    // ---------- CSV import (preview + commit) ----------

    public record ImportRow(int RowNumber, string Sku, string Name, string CategoryName, string UnitSymbol,
        string? Barcode, decimal? CostPrice, decimal? SalePrice, decimal ReorderPoint,
        bool TrackBatches, bool TrackExpiry, string? Error);

    public record ImportPreview(int TotalRows, int ValidRows, int ErrorRows, IReadOnlyList<ImportRow> Rows);

    public async Task<ImportPreview> PreviewImportAsync(Stream csvStream, CancellationToken ct)
    {
        var rows = await ParseImportCsvAsync(csvStream, ct);
        return await BuildPreviewAsync(rows, ct);
    }

    public record ImportCommitResult(int Created, int Skipped, IReadOnlyList<ImportRow> Errors);

    public async Task<ImportCommitResult> CommitImportAsync(Stream csvStream, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsCreate, ct);
        var rows = await ParseImportCsvAsync(csvStream, ct);
        var preview = await BuildPreviewAsync(rows, ct);

        var categoryCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in preview.Rows.Where(r => r.Error == null))
        {
            Guid categoryId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(row.CategoryName))
            {
                if (!categoryCache.TryGetValue(row.CategoryName, out categoryId))
                {
                    var cat = await _db.Categories.FirstOrDefaultAsync(
                        c => c.TenantId == TenantId && c.Name == row.CategoryName.Trim(), ct);
                    if (cat == null)
                    {
                        cat = new Category { TenantId = TenantId, Name = row.CategoryName.Trim() };
                        _db.Categories.Add(cat);
                        await _db.SaveChangesAsync(ct);
                    }
                    categoryCache[row.CategoryName] = cat.Id;
                    categoryId = cat.Id;
                }
            }

            var unit = await _db.Units.AsNoTracking()
                .FirstOrDefaultAsync(u => u.TenantId == TenantId && u.Symbol == row.UnitSymbol.Trim().ToLowerInvariant(), ct)
                ?? throw AppException.Validation($"Row {row.RowNumber}: unit '{row.UnitSymbol}' not found.");

            if (await _db.Products.AnyAsync(p => p.TenantId == TenantId && p.Sku == row.Sku.Trim().ToUpperInvariant(), ct))
                throw AppException.Conflict($"Row {row.RowNumber}: SKU '{row.Sku}' already exists. Import aborted; no rows were committed.");

            _db.Products.Add(new Product
            {
                TenantId = TenantId,
                Sku = row.Sku.Trim().ToUpperInvariant(),
                Name = row.Name.Trim(),
                Barcode = string.IsNullOrWhiteSpace(row.Barcode) ? null : row.Barcode.Trim(),
                CategoryId = categoryId == Guid.Empty ? null : categoryId,
                UnitId = unit.Id,
                CostPrice = row.CostPrice,
                SalePrice = row.SalePrice,
                ReorderPoint = row.ReorderPoint,
                TrackBatches = row.TrackBatches,
                TrackExpiry = row.TrackExpiry,
                CreatedBy = _currentUser.UserId
            });
        }

        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Product.Imported", "Product", null, new { created = preview.ValidRows }, ct);
        return new ImportCommitResult(preview.ValidRows, preview.ErrorRows, preview.Rows.Where(r => r.Error != null).ToList());
    }

    public async Task<(byte[] content, string fileName)> ExportCsvAsync(ProductListQuery q, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.ProductsRead, ct);
        // Export everything matching the filter (no pagination).
        var all = await ListAsync(q with { Page = 1, PageSize = QueryableExtensions.MaxPageSize }, ct);
        // Page through the rest if more remain.
        var items = new List<ProductDto>(all.Items);
        for (var p = 2; p <= all.TotalPages; p++)
        {
            var next = await ListAsync(q with { Page = p, PageSize = QueryableExtensions.MaxPageSize }, ct);
            items.AddRange(next.Items);
        }

        using var sw = new StringWriter();
        using var csv = new CsvWriter(sw, new CsvConfiguration(CultureInfo.InvariantCulture));
        csv.WriteField("SKU"); csv.WriteField("Name"); csv.WriteField("Barcode"); csv.WriteField("Category");
        csv.WriteField("Brand"); csv.WriteField("Unit"); csv.WriteField("CostPrice"); csv.WriteField("SalePrice");
        csv.WriteField("ReorderPoint"); csv.WriteField("TrackBatches"); csv.WriteField("TrackExpiry");
        csv.WriteField("Status"); csv.WriteField("OnHand");
        await csv.NextRecordAsync();
        foreach (var p in items)
        {
            csv.WriteField(p.Sku); csv.WriteField(p.Name); csv.WriteField(p.Barcode); csv.WriteField(p.CategoryName);
            csv.WriteField(p.Brand); csv.WriteField(p.UnitSymbol); csv.WriteField(p.CostPrice); csv.WriteField(p.SalePrice);
            csv.WriteField(p.ReorderPoint); csv.WriteField(p.TrackBatches); csv.WriteField(p.TrackExpiry);
            csv.WriteField(p.Status); csv.WriteField(p.OnHandTotal);
            await csv.NextRecordAsync();
        }
        return (Encoding.UTF8.GetBytes(sw.ToString()), $"products-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    private async Task<List<ImportRow>> ParseImportCsvAsync(Stream stream, CancellationToken ct)
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture) { HeaderValidated = null, MissingFieldFound = null, TrimOptions = TrimOptions.Trim };
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, config);
        var rows = new List<ImportRow>();
        await csv.ReadAsync();
        csv.ReadHeader();
        if (csv.HeaderRecord == null || !csv.HeaderRecord.Any(h => h?.Equals("SKU", StringComparison.OrdinalIgnoreCase) == true))
            throw AppException.Validation("CSV must contain a 'SKU' header column.");
        if (!csv.HeaderRecord.Any(h => h?.Equals("Name", StringComparison.OrdinalIgnoreCase) == true))
            throw AppException.Validation("CSV must contain a 'Name' header column.");

        var rowNumber = 1;
        while (await csv.ReadAsync())
        {
            rowNumber++;
            bool? trackBatches = null, trackExpiry = null;
            var tb = csv.GetField("TrackBatches");
            var te = csv.GetField("TrackExpiry");
            if (bool.TryParse(tb, out var tbv)) trackBatches = tbv;
            if (bool.TryParse(te, out var tev)) trackExpiry = tev;
            rows.Add(new ImportRow(rowNumber,
                csv.GetField("SKU") ?? "",
                csv.GetField("Name") ?? "",
                csv.GetField("Category"),
                csv.GetField("Unit") ?? "pcs",
                csv.GetField("Barcode"),
                ParseDecimal(csv.GetField("CostPrice")),
                ParseDecimal(csv.GetField("SalePrice")),
                ParseDecimal(csv.GetField("ReorderPoint")) ?? 0m,
                trackBatches ?? false, trackExpiry ?? false, null));
        }
        return rows;

        static decimal? ParseDecimal(string? s) =>
            decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    private async Task<ImportPreview> BuildPreviewAsync(List<ImportRow> rows, CancellationToken ct)
    {
        var skus = rows.Select(r => r.Sku.Trim().ToUpperInvariant()).Where(s => s != "").ToList();
        var existingSkus = await _db.Products.AsNoTracking()
            .Where(p => p.TenantId == TenantId && skus.Contains(p.Sku)).Select(p => p.Sku).ToListAsync(ct);
        var existingSkuSet = existingSkus.ToHashSet();
        var units = await _db.Units.AsNoTracking().Where(u => u.TenantId == TenantId).ToListAsync(ct);
        var unitSymbols = units.Select(u => u.Symbol).ToHashSet();
        var categories = await _db.Categories.AsNoTracking().Where(c => c.TenantId == TenantId).ToListAsync(ct);
        var categoryNames = categories.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seenSkus = new HashSet<string>();
        var importUnitSymbols = rows.Select(r => r.UnitSymbol?.Trim().ToLowerInvariant()).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();

        // Unknown units create them on commit for convenience? No: report as error so import stays explicit.
        List<ImportRow> result = new();
        foreach (var r in rows)
        {
            string? error = null;
            if (string.IsNullOrWhiteSpace(r.Sku)) error = "SKU is required.";
            else if (string.IsNullOrWhiteSpace(r.Name)) error = "Name is required.";
            else if (!seenSkus.Add(r.Sku.Trim().ToUpperInvariant())) error = $"Duplicate SKU '{r.Sku}' within file.";
            else if (existingSkuSet.Contains(r.Sku.Trim().ToUpperInvariant())) error = $"SKU '{r.Sku}' already exists.";
            else if (string.IsNullOrWhiteSpace(r.UnitSymbol) || !unitSymbols.Contains(r.UnitSymbol.Trim().ToLowerInvariant()))
                error = $"Unit '{r.UnitSymbol}' not found. Create it first.";
            else if (!string.IsNullOrWhiteSpace(r.CategoryName) && !categoryNames.Contains(r.CategoryName.Trim()))
                error = $"Category '{r.CategoryName}' not found (it will not be auto-created).";
            result.Add(r with { Error = error });
        }
        return new ImportPreview(result.Count, result.Count(r => r.Error == null), result.Count(r => r.Error != null), result);
    }

    private async Task ValidateReferencesAsync(SaveProductRequest req, CancellationToken ct)
    {
        if (!await _db.Units.AnyAsync(u => u.Id == req.UnitId && u.TenantId == TenantId, ct))
            throw AppException.Validation("Unit not found.", new Dictionary<string, string[]> { ["unitId"] = ["Unknown unit."] });
        if (req.CategoryId is { } cat && !await _db.Categories.AnyAsync(c => c.Id == cat && c.TenantId == TenantId, ct))
            throw AppException.Validation("Category not found.", new Dictionary<string, string[]> { ["categoryId"] = ["Unknown category."] });
        if (req.DefaultSupplierId is { } sup && !await _db.Suppliers.AnyAsync(s => s.Id == sup && s.TenantId == TenantId, ct))
            throw AppException.Validation("Supplier not found.", new Dictionary<string, string[]> { ["defaultSupplierId"] = ["Unknown supplier."] });
        if (req.TrackExpiry && !req.TrackBatches)
            throw AppException.Validation("Expiry tracking requires batch tracking.",
                new Dictionary<string, string[]> { ["trackExpiry"] = ["Expiry tracking requires batch tracking."] });
    }

    private async Task EnsureUniqueAsync(string sku, string? barcode, Guid? excludeId, CancellationToken ct)
    {
        var skuNorm = sku.Trim().ToUpperInvariant();
        if (await _db.Products.AnyAsync(p => p.TenantId == TenantId && p.Sku == skuNorm && (excludeId == null || p.Id != excludeId), ct))
            throw AppException.Conflict($"A product with SKU '{skuNorm}' already exists.");
        if (!string.IsNullOrWhiteSpace(barcode))
        {
            var bc = barcode.Trim();
            if (await _db.Products.AnyAsync(p => p.TenantId == TenantId && p.Barcode == bc && (excludeId == null || p.Id != excludeId), ct))
                throw AppException.Conflict($"A product with barcode '{bc}' already exists.");
        }
    }

    private static Product MapToEntity(SaveProductRequest req) => new()
    {
        Sku = req.Sku.Trim().ToUpperInvariant(),
        Barcode = string.IsNullOrWhiteSpace(req.Barcode) ? null : req.Barcode.Trim(),
        Name = req.Name.Trim(),
        Description = req.Description,
        ImageUrl = req.ImageUrl,
        CategoryId = req.CategoryId,
        Brand = req.Brand,
        UnitId = req.UnitId,
        CostPrice = req.CostPrice,
        SalePrice = req.SalePrice,
        ReorderPoint = req.ReorderPoint,
        TargetStockLevel = req.TargetStockLevel,
        DefaultSupplierId = req.DefaultSupplierId,
        TrackBatches = req.TrackBatches,
        TrackExpiry = req.TrackExpiry,
        MinShelfLifeDays = req.MinShelfLifeDays
    };

    internal static ProductDto ToDto(Product p, UnitOfMeasure unit, string? category, string? supplier,
        decimal onHand, string currency) => new(
        p.Id, p.Sku, p.Barcode, p.Name, p.Description, p.ImageUrl, p.CategoryId, category, p.Brand,
        p.UnitId, unit.Name, unit.Symbol, p.CostPrice, p.SalePrice, currency, p.CostMethod.ToString(),
        p.ReorderPoint, p.TargetStockLevel, p.DefaultSupplierId, supplier,
        p.TrackBatches, p.TrackExpiry, p.MinShelfLifeDays, p.Status.ToString(), onHand, p.CreatedAt);
}
