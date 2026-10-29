using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockWise.Domain.Entities;

namespace StockWise.Infrastructure.Persistence.Configurations;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(150).IsRequired();
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.ParentCategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class UnitOfMeasureConfiguration : IEntityTypeConfiguration<UnitOfMeasure>
{
    public void Configure(EntityTypeBuilder<UnitOfMeasure> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Symbol }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Symbol).HasMaxLength(30).IsRequired();
    }
}

public class UnitConversionConfiguration : IEntityTypeConfiguration<UnitConversion>
{
    public void Configure(EntityTypeBuilder<UnitConversion> b)
    {
        b.Property(x => x.Factor).HasPrecision(18, 6);
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => x.FromUnitId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => x.ToUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IsActive });
        b.Property(x => x.Code).HasMaxLength(50).IsRequired();
        b.Property(x => x.Name).HasMaxLength(200).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3);
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Sku }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Barcode }).IsUnique().HasFilter("barcode IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.CategoryId });
        b.HasIndex(x => new { x.TenantId, x.DefaultSupplierId });
        b.HasIndex(x => new { x.TenantId, x.ReorderPoint });
        b.Property(x => x.Sku).HasMaxLength(80).IsRequired();
        b.Property(x => x.Barcode).HasMaxLength(80);
        b.Property(x => x.Name).HasMaxLength(300).IsRequired();
        b.Property(x => x.CostPrice).HasPrecision(18, 4);
        b.Property(x => x.SalePrice).HasPrecision(18, 4);
        b.Property(x => x.ReorderPoint).HasPrecision(18, 4);
        b.Property(x => x.TargetStockLevel).HasPrecision(18, 4);
        b.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<UnitOfMeasure>().WithMany().HasForeignKey(x => x.UnitId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.DefaultSupplierId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class ProductSupplierConfiguration : IEntityTypeConfiguration<ProductSupplier>
{
    public void Configure(EntityTypeBuilder<ProductSupplier> b)
    {
        b.HasIndex(x => new { x.TenantId, x.ProductId, x.SupplierId }).IsUnique();
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Cascade);
    }
}
