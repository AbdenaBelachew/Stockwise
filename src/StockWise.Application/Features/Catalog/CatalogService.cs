using FluentValidation;
using Microsoft.EntityFrameworkCore;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Domain.Entities;
using StockWise.Domain.Exceptions;

namespace StockWise.Application.Features.Catalog;

public record CategoryDto(Guid Id, string Name, Guid? ParentCategoryId, string? ParentCategoryName, string? Description, bool IsActive);
public record CreateCategoryRequest(string Name, Guid? ParentCategoryId, string? Description);
public record UpdateCategoryRequest(string Name, Guid? ParentCategoryId, string? Description, bool IsActive);
public record UnitDto(Guid Id, string Name, string Symbol, bool AllowsFractional);
public record CreateUnitRequest(string Name, string Symbol, bool AllowsFractional);
public record UpdateUnitRequest(string Name, string Symbol, bool AllowsFractional);
public record WarehouseDto(Guid Id, string Name, string Code, string? Address, string? City, string? Country, bool IsActive);
public record CreateWarehouseRequest(string Name, string Code, string? Address, string? City, string? Country, bool IsActive = true);
public record UpdateWarehouseRequest(string Name, string Code, string? Address, string? City, string? Country, bool IsActive);

public class CreateCategoryValidator : AbstractValidator<CreateCategoryRequest>
{
    public CreateCategoryValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(150);
    }
}

public class CreateUnitValidator : AbstractValidator<CreateUnitRequest>
{
    public CreateUnitValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Symbol).NotEmpty().MaximumLength(30);
    }
}

public class CreateWarehouseValidator : AbstractValidator<CreateWarehouseRequest>
{
    public CreateWarehouseValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50).Matches("^[A-Za-z0-9_-]+$")
            .WithMessage("Code may only contain letters, digits, dashes and underscores.");
    }
}

public class CatalogService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly IAuditService _audit;

    public CatalogService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    // ---------- Categories ----------

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = _db.Categories.AsNoTracking().Where(c => c.TenantId == TenantId);
        if (!includeInactive) q = q.Where(c => c.IsActive);
        var list = await q.OrderBy(c => c.Name).ToListAsync(ct);
        var byId = list.ToDictionary(c => c.Id);
        return list.Select(c => new CategoryDto(c.Id, c.Name, c.ParentCategoryId,
            c.ParentCategoryId is { } pid && byId.TryGetValue(pid, out var p) ? p.Name : null,
            c.Description, c.IsActive)).ToList();
    }

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.CatalogManage, ct);
        if (req.ParentCategoryId is { } pid &&
            !await _db.Categories.AnyAsync(c => c.Id == pid && c.TenantId == TenantId, ct))
            throw AppException.Validation("Parent category not found.",
                new Dictionary<string, string[]> { ["parentCategoryId"] = ["Unknown parent category."] });
        if (await _db.Categories.AnyAsync(c => c.TenantId == TenantId && c.Name == req.Name.Trim(), ct))
            throw AppException.Conflict($"A category named '{req.Name.Trim()}' already exists.");

        var cat = new Category { TenantId = TenantId, Name = req.Name.Trim(), ParentCategoryId = req.ParentCategoryId, Description = req.Description };
        _db.Categories.Add(cat);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Category.Created", "Category", cat.Id, new { cat.Name }, ct);
        return new CategoryDto(cat.Id, cat.Name, cat.ParentCategoryId, null, cat.Description, cat.IsActive);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid id, UpdateCategoryRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.CatalogManage, ct);
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Category");
        if (req.ParentCategoryId == id)
            throw AppException.Validation("A category cannot be its own parent.",
                new Dictionary<string, string[]> { ["parentCategoryId"] = ["Invalid parent."] });
        if (await _db.Categories.AnyAsync(c => c.TenantId == TenantId && c.Name == req.Name.Trim() && c.Id != id, ct))
            throw AppException.Conflict($"A category named '{req.Name.Trim()}' already exists.");

        cat.Name = req.Name.Trim();
        cat.ParentCategoryId = req.ParentCategoryId;
        cat.Description = req.Description;
        cat.IsActive = req.IsActive;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Category.Updated", "Category", id, req, ct);
        return new CategoryDto(cat.Id, cat.Name, cat.ParentCategoryId, null, cat.Description, cat.IsActive);
    }

    // Categories with products or subcategories are archived, never deleted (history preservation rule).
    public async Task ArchiveCategoryAsync(Guid id, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.CatalogManage, ct);
        var cat = await _db.Categories.FirstOrDefaultAsync(c => c.Id == id && c.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Category");
        cat.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Category.Archived", "Category", id, null, ct);
    }

    // ---------- Units ----------

    public async Task<IReadOnlyList<UnitDto>> ListUnitsAsync(CancellationToken ct) =>
        await _db.Units.AsNoTracking().Where(u => u.TenantId == TenantId)
            .OrderBy(u => u.Name)
            .Select(u => new UnitDto(u.Id, u.Name, u.Symbol, u.AllowsFractional))
            .ToListAsync(ct);

    public async Task<UnitDto> CreateUnitAsync(CreateUnitRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.CatalogManage, ct);
        if (await _db.Units.AnyAsync(u => u.TenantId == TenantId && u.Symbol == req.Symbol.Trim().ToLowerInvariant(), ct))
            throw AppException.Conflict($"A unit with symbol '{req.Symbol}' already exists.");
        var unit = new UnitOfMeasure
        {
            TenantId = TenantId, Name = req.Name.Trim(),
            Symbol = req.Symbol.Trim().ToLowerInvariant(), AllowsFractional = req.AllowsFractional
        };
        _db.Units.Add(unit);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Unit.Created", "UnitOfMeasure", unit.Id, new { unit.Name }, ct);
        return new UnitDto(unit.Id, unit.Name, unit.Symbol, unit.AllowsFractional);
    }

    public async Task<UnitDto> UpdateUnitAsync(Guid id, UpdateUnitRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.CatalogManage, ct);
        var unit = await _db.Units.FirstOrDefaultAsync(u => u.Id == id && u.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Unit");
        if (await _db.Units.AnyAsync(u => u.TenantId == TenantId && u.Symbol == req.Symbol.Trim().ToLowerInvariant() && u.Id != id, ct))
            throw AppException.Conflict($"A unit with symbol '{req.Symbol}' already exists.");
        unit.Name = req.Name.Trim();
        unit.Symbol = req.Symbol.Trim().ToLowerInvariant();
        unit.AllowsFractional = req.AllowsFractional;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Unit.Updated", "UnitOfMeasure", id, req, ct);
        return new UnitDto(unit.Id, unit.Name, unit.Symbol, unit.AllowsFractional);
    }

    // ---------- Warehouses ----------

    public async Task<IReadOnlyList<WarehouseDto>> ListWarehousesAsync(bool includeInactive, CancellationToken ct)
    {
        var q = _db.Warehouses.AsNoTracking().Where(w => w.TenantId == TenantId);
        if (!includeInactive) q = q.Where(w => w.IsActive);
        return await q.OrderBy(w => w.Name)
            .Select(w => new WarehouseDto(w.Id, w.Name, w.Code, w.Address, w.City, w.Country, w.IsActive))
            .ToListAsync(ct);
    }

    public async Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.WarehousesManage, ct);
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.TenantId == TenantId && w.Code == code, ct))
            throw AppException.Conflict($"A warehouse with code '{code}' already exists.");

        var wh = new Warehouse
        {
            TenantId = TenantId, Name = req.Name.Trim(), Code = code, Address = req.Address,
            City = req.City, Country = req.Country, IsActive = req.IsActive, CreatedBy = _currentUser.UserId
        };
        _db.Warehouses.Add(wh);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Warehouse.Created", "Warehouse", wh.Id, new { wh.Code }, ct);
        return new WarehouseDto(wh.Id, wh.Name, wh.Code, wh.Address, wh.City, wh.Country, wh.IsActive);
    }

    public async Task<WarehouseDto> UpdateWarehouseAsync(Guid id, UpdateWarehouseRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.WarehousesManage, ct);
        var wh = await _db.Warehouses.FirstOrDefaultAsync(w => w.Id == id && w.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Warehouse");
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _db.Warehouses.AnyAsync(w => w.TenantId == TenantId && w.Code == code && w.Id != id, ct))
            throw AppException.Conflict($"A warehouse with code '{code}' already exists.");

        wh.Name = req.Name.Trim(); wh.Code = code; wh.Address = req.Address;
        wh.City = req.City; wh.Country = req.Country; wh.IsActive = req.IsActive;
        wh.UpdatedBy = _currentUser.UserId; wh.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Warehouse.Updated", "Warehouse", id, req, ct);
        return new WarehouseDto(wh.Id, wh.Name, wh.Code, wh.Address, wh.City, wh.Country, wh.IsActive);
    }
}

public record SupplierDto(
    Guid Id, string Code, string Name, string? ContactPerson, string? Email, string? Phone,
    string? Address, string? Country, string? TaxId, int PaymentTermsDays, string? CurrencyCode,
    string? Notes, bool IsActive, DateTime CreatedAt);

public record SaveSupplierRequest(
    string Code, string Name, string? ContactPerson, string? Email, string? Phone,
    string? Address, string? Country, string? TaxId, int PaymentTermsDays, string? CurrencyCode, string? Notes);

public class SaveSupplierValidator : AbstractValidator<SaveSupplierRequest>
{
    public SaveSupplierValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().When(x => !string.IsNullOrEmpty(x.Email));
        RuleFor(x => x.PaymentTermsDays).InclusiveBetween(0, 365);
        RuleFor(x => x.CurrencyCode).Length(3).When(x => !string.IsNullOrEmpty(x.CurrencyCode));
    }
}

public class SupplierService
{
    private readonly IAppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IPermissionChecker _permissions;
    private readonly IAuditService _audit;

    public SupplierService(IAppDbContext db, ICurrentUser currentUser, IPermissionChecker permissions, IAuditService audit)
    {
        _db = db; _currentUser = currentUser; _permissions = permissions; _audit = audit;
    }

    private Guid TenantId => _currentUser.TenantId ?? throw AppException.Unauthorized();

    public async Task<PageResult<SupplierDto>> ListAsync(string? search, bool? active, int page, int pageSize, CancellationToken ct)
    {
        var q = _db.Suppliers.AsNoTracking().Where(s => s.TenantId == TenantId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            q = q.Where(s => s.Name.ToLower().Contains(term) || s.Code.ToLower().Contains(term)
                || (s.Email != null && s.Email.ToLower().Contains(term)));
        }
        if (active is { } a) q = q.Where(s => s.IsActive == a);
        var pageQ = q.OrderBy(s => s.Name).Select(s => ToDto(s));
        return await pageQ.ToPageResultAsync(page, pageSize, ct);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct)
    {
        var s = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Supplier");
        return ToDto(s);
    }

    public async Task<SupplierDto> CreateAsync(SaveSupplierRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.SuppliersManage, ct);
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _db.Suppliers.AnyAsync(s => s.TenantId == TenantId && s.Code == code, ct))
            throw AppException.Conflict($"A supplier with code '{code}' already exists.");

        var s = new Supplier
        {
            TenantId = TenantId, Code = code, Name = req.Name.Trim(), ContactPerson = req.ContactPerson,
            Email = req.Email, Phone = req.Phone, Address = req.Address, Country = req.Country,
            TaxId = req.TaxId, PaymentTermsDays = req.PaymentTermsDays, CurrencyCode = req.CurrencyCode?.ToUpperInvariant(),
            Notes = req.Notes, CreatedBy = _currentUser.UserId
        };
        _db.Suppliers.Add(s);
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Supplier.Created", "Supplier", s.Id, new { s.Code, s.Name }, ct);
        return ToDto(s);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SaveSupplierRequest req, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.SuppliersManage, ct);
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Supplier");
        var code = req.Code.Trim().ToUpperInvariant();
        if (await _db.Suppliers.AnyAsync(x => x.TenantId == TenantId && x.Code == code && x.Id != id, ct))
            throw AppException.Conflict($"A supplier with code '{code}' already exists.");

        s.Code = code; s.Name = req.Name.Trim(); s.ContactPerson = req.ContactPerson; s.Email = req.Email;
        s.Phone = req.Phone; s.Address = req.Address; s.Country = req.Country; s.TaxId = req.TaxId;
        s.PaymentTermsDays = req.PaymentTermsDays; s.CurrencyCode = req.CurrencyCode?.ToUpperInvariant();
        s.Notes = req.Notes; s.UpdatedBy = _currentUser.UserId; s.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Supplier.Updated", "Supplier", id, req, ct);
        return ToDto(s);
    }

    /// <summary>Suppliers with purchase history are archived, not deleted.</summary>
    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        await _permissions.EnsurePermissionAsync(_currentUser.UserId!.Value, TenantId, Permissions.SuppliersManage, ct);
        var s = await _db.Suppliers.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == TenantId, ct)
            ?? throw AppException.NotFound("Supplier");
        s.IsActive = false;
        await _db.SaveChangesAsync(ct);
        await _audit.WriteAsync("Supplier.Archived", "Supplier", id, null, ct);
    }

    internal static SupplierDto ToDto(Supplier s) => new(
        s.Id, s.Code, s.Name, s.ContactPerson, s.Email, s.Phone, s.Address, s.Country,
        s.TaxId, s.PaymentTermsDays, s.CurrencyCode, s.Notes, s.IsActive, s.CreatedAt);
}
