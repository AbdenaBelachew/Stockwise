using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

public class Category : TenantEntity
{
    public string Name { get; set; } = default!;
    public Guid? ParentCategoryId { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class UnitOfMeasure : TenantEntity
{
    public string Name { get; set; } = default!;      // e.g. Piece
    public string Symbol { get; set; } = default!;    // e.g. pcs
    public bool AllowsFractional { get; set; }
}

public class UnitConversion : TenantEntity
{
    public Guid FromUnitId { get; set; }
    public Guid ToUnitId { get; set; }
    /// <summary>1 FromUnit = Factor ToUnits. Must be > 0.</summary>
    public decimal Factor { get; set; }
}

public class Warehouse : TenantEntity, ICreatable, IUpdatable
{
    public string Name { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public bool IsActive { get; set; } = true;
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
