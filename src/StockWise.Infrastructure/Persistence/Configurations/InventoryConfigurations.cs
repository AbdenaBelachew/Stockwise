using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockWise.Domain.Entities;

namespace StockWise.Infrastructure.Persistence.Configurations;

public class InventoryBalanceConfiguration : IEntityTypeConfiguration<InventoryBalance>
{
    public void Configure(EntityTypeBuilder<InventoryBalance> b)
    {
        // PostgreSQL system column used as an optimistic concurrency token; combined with
        // SELECT ... FOR UPDATE row locking when posting movements.
        b.Property<uint>("xmin").IsRowVersion();
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.WarehouseId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.WarehouseId });
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.Property(x => x.AvgUnitCost).HasPrecision(18, 4);
        b.HasIndex(x => new { x.TenantId, x.Quantity });
    }
}

public class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> b)
    {
        // Immutability of posted movements is additionally enforced by a database trigger
        // created in migrations (blocks UPDATE/DELETE on inventory_movements).
        b.ToTable("inventory_movements");
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.WarehouseId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.ReferenceType, x.ReferenceId });
        b.HasIndex(x => new { x.TenantId, x.MovementType });
        b.HasIndex(x => new { x.TenantId, x.WarehouseId, x.CreatedAtUtc });
        b.Property(x => x.QuantityDelta).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.ReferenceType).HasMaxLength(60);
        b.Property(x => x.ReferenceNumber).HasMaxLength(60);
        b.Property(x => x.Reason).HasMaxLength(500);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class BatchConfiguration : IEntityTypeConfiguration<Batch>
{
    public void Configure(EntityTypeBuilder<Batch> b)
    {
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.WarehouseId, x.BatchNumber }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ExpiryDate });
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.WarehouseId, x.ExpiryDate });
        b.Property(x => x.BatchNumber).HasMaxLength(120).IsRequired();
        b.Property(x => x.InitialQuantity).HasPrecision(18, 4);
        b.Property(x => x.QuantityRemaining).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    public void Configure(EntityTypeBuilder<NumberSequence> b)
    {
        b.HasIndex(x => new { x.TenantId, x.SequenceType }).IsUnique();
        b.Property(x => x.SequenceType).HasMaxLength(50).IsRequired();
    }
}
