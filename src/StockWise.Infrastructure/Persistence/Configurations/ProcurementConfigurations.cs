using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockWise.Domain.Entities;

namespace StockWise.Infrastructure.Persistence.Configurations;

public class PurchaseOrderConfiguration : IEntityTypeConfiguration<PurchaseOrder>
{
    public void Configure(EntityTypeBuilder<PurchaseOrder> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.SupplierId });
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
        b.Property(x => x.SubTotal).HasPrecision(18, 4);
        b.Property(x => x.TaxTotal).HasPrecision(18, 4);
        b.Property(x => x.Total).HasPrecision(18, 4);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Approvals).WithOne().HasForeignKey(a => a.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class PurchaseOrderLineConfiguration : IEntityTypeConfiguration<PurchaseOrderLine>
{
    public void Configure(EntityTypeBuilder<PurchaseOrderLine> b)
    {
        b.HasIndex(x => new { x.PurchaseOrderId, x.ProductId });
        b.Property(x => x.OrderedQuantity).HasPrecision(18, 4);
        b.Property(x => x.ReceivedQuantity).HasPrecision(18, 4);
        b.Property(x => x.UnitCost).HasPrecision(18, 4);
        b.Property(x => x.DiscountPercent).HasPrecision(9, 4);
        b.Property(x => x.TaxPercent).HasPrecision(9, 4);
        b.Property(x => x.LineTotal).HasPrecision(18, 4);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PurchaseApprovalConfiguration : IEntityTypeConfiguration<PurchaseApproval>
{
    public void Configure(EntityTypeBuilder<PurchaseApproval> b)
    {
        b.HasIndex(x => new { x.PurchaseOrderId, x.AtUtc });
    }
}

public class GoodsReceiptConfiguration : IEntityTypeConfiguration<GoodsReceipt>
{
    public void Configure(EntityTypeBuilder<GoodsReceipt> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique().HasFilter("idempotency_key IS NOT NULL");
        b.HasIndex(x => new { x.TenantId, x.PurchaseOrderId });
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.IdempotencyKey).HasMaxLength(120);
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<PurchaseOrder>().WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.GoodsReceiptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class GoodsReceiptLineConfiguration : IEntityTypeConfiguration<GoodsReceiptLine>
{
    public void Configure(EntityTypeBuilder<GoodsReceiptLine> b)
    {
        b.Property(x => x.QuantityAccepted).HasPrecision(18, 4);
        b.Property(x => x.QuantityRejected).HasPrecision(18, 4);
        b.Property(x => x.BatchNumber).HasMaxLength(120);
        b.Property(x => x.RejectReason).HasMaxLength(500);
        b.HasOne<PurchaseOrderLine>().WithMany().HasForeignKey(x => x.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
    }
}
