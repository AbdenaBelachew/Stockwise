using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

public class Product : TenantEntity, ICreatable, IUpdatable
{
    public string Sku { get; set; } = default!;
    public string? Barcode { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string? ImageUrl { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Brand { get; set; }
    public Guid UnitId { get; set; }
    public decimal? CostPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public CostMethod CostMethod { get; set; } = CostMethod.WeightedAverage;
    public decimal ReorderPoint { get; set; }
    public decimal? TargetStockLevel { get; set; }
    public Guid? DefaultSupplierId { get; set; }
    public bool TrackBatches { get; set; }
    public bool TrackExpiry { get; set; }
    /// <summary>Minimum remaining shelf-life in days required to receive this product; null = no requirement.</summary>
    public int? MinShelfLifeDays { get; set; }
    public ProductStatus Status { get; set; } = ProductStatus.Active;

    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ProductSupplier : TenantEntity
{
    public Guid ProductId { get; set; }
    public Guid SupplierId { get; set; }
    public string? SupplierSku { get; set; }
    public decimal? UnitCost { get; set; }
    public int? LeadTimeDays { get; set; }
    public bool IsPreferred { get; set; }
}

public class Supplier : TenantEntity, ICreatable, IUpdatable
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? ContactPerson { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Country { get; set; }
    public string? TaxId { get; set; }
    public int PaymentTermsDays { get; set; }
    public string? CurrencyCode { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
