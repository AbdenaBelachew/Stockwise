using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;

namespace StockWise.Infrastructure.Persistence.Configurations;

public class StockTransferConfiguration : IEntityTypeConfiguration<StockTransfer>
{
    public void Configure(EntityTypeBuilder<StockTransfer> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.SourceWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.DestinationWarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StockTransferId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockTransferLineConfiguration : IEntityTypeConfiguration<StockTransferLine>
{
    public void Configure(EntityTypeBuilder<StockTransferLine> b)
    {
        b.Property(x => x.Quantity).HasPrecision(18, 4);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class StockCountConfiguration : IEntityTypeConfiguration<StockCount>
{
    public void Configure(EntityTypeBuilder<StockCount> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Number }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.Status });
        b.HasIndex(x => new { x.TenantId, x.CreatedAt });
        b.Property(x => x.Number).HasMaxLength(40).IsRequired();
        b.Property(x => x.Notes).HasMaxLength(2000);
        b.HasOne<Warehouse>().WithMany().HasForeignKey(x => x.WarehouseId).OnDelete(DeleteBehavior.Restrict);
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.StockCountId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class StockCountLineConfiguration : IEntityTypeConfiguration<StockCountLine>
{
    public void Configure(EntityTypeBuilder<StockCountLine> b)
    {
        b.Property(x => x.ExpectedQuantity).HasPrecision(18, 4);
        b.Property(x => x.CountedQuantity).HasPrecision(18, 4);
        b.HasIndex(x => new { x.StockCountId, x.ProductId });
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Batch>().WithMany().HasForeignKey(x => x.BatchId).OnDelete(DeleteBehavior.SetNull);
    }
}

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> b)
    {
        // DedupKey is unique per tenant while the alert is unresolved; sweeping logic
        // refreshes CreatedAt instead of inserting duplicates.
        b.HasIndex(x => new { x.TenantId, x.DedupKey, x.ResolvedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        // The notification list only reads unresolved alerts, newest first.
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc })
            .HasFilter("resolved_at_utc IS NULL")
            .HasDatabaseName("ix_notifications_unresolved_by_date");
        b.Property(x => x.Type).HasMaxLength(60).IsRequired();
        b.Property(x => x.Title).HasMaxLength(200).IsRequired();
        b.Property(x => x.Message).HasMaxLength(1000).IsRequired();
        b.Property(x => x.DedupKey).HasMaxLength(200).IsRequired();
        b.Property(x => x.Link).HasMaxLength(300);
    }
}

public class NotificationReadConfiguration : IEntityTypeConfiguration<NotificationRead>
{
    public void Configure(EntityTypeBuilder<NotificationRead> b)
    {
        b.HasIndex(x => new { x.NotificationId, x.UserId }).IsUnique();
        b.HasOne<Notification>().WithMany().HasForeignKey(x => x.NotificationId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> b)
    {
        b.HasIndex(x => new { x.TenantId, x.CreatedAtUtc });
        b.Property(x => x.Action).HasMaxLength(120).IsRequired();
        b.Property(x => x.EntityType).HasMaxLength(80).IsRequired();
        b.Property(x => x.EntityId).HasMaxLength(60);
        b.Property(x => x.Details).HasColumnType("jsonb");
        b.Property(x => x.ActorEmail).HasMaxLength(320).IsRequired();
        b.Property(x => x.IpAddress).HasMaxLength(60);
    }
}

public class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> b)
    {
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Code).HasMaxLength(40).IsRequired();
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.PriceMonthly).HasPrecision(18, 2);
    }
}

public class TenantSubscriptionConfiguration : IEntityTypeConfiguration<TenantSubscription>
{
    public void Configure(EntityTypeBuilder<TenantSubscription> b)
    {
        b.HasIndex(x => x.OrganizationId).IsUnique();
        b.HasOne<Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> b)
    {
        b.HasIndex(x => new { x.TenantId, x.Key }).IsUnique();
        b.Property(x => x.Key).HasMaxLength(120).IsRequired();
        b.Property(x => x.Endpoint).HasMaxLength(200).IsRequired();
    }
}

public class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> b)
    {
        b.Property(x => x.To).HasMaxLength(320).IsRequired();
        b.Property(x => x.Subject).HasMaxLength(300).IsRequired();
    }
}

public static class DecimalDefaults
{
    public const string Precision = "decimal(18,4)";
}
