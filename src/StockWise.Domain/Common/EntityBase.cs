using StockWise.Domain.Enums;

namespace StockWise.Domain.Common;

/// <summary>Base class for entities scoped to an organization (tenant).</summary>
public abstract class TenantEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
}

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.NewGuid();
}

public interface ICreatable
{
    Guid? CreatedBy { get; set; }
    DateTime CreatedAt { get; set; }
}

public interface IUpdatable
{
    Guid? UpdatedBy { get; set; }
    DateTime? UpdatedAt { get; set; }
}
