using Microsoft.EntityFrameworkCore;
using StockWise.Domain.Entities;

namespace StockWise.Application.Abstractions;

/// <summary>Persistence abstraction used by application services. Implemented by the EF Core DbContext.</summary>
public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<RefreshToken> RefreshTokens { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<Organization> Organizations { get; }
    DbSet<TenantMembership> TenantMemberships { get; }
    DbSet<Role> Roles { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<Invitation> Invitations { get; }
    DbSet<Category> Categories { get; }
    DbSet<UnitOfMeasure> Units { get; }
    DbSet<UnitConversion> UnitConversions { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<Product> Products { get; }
    DbSet<ProductSupplier> ProductSuppliers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<Batch> Batches { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<PurchaseApproval> PurchaseApprovals { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<StockCount> StockCounts { get; }
    DbSet<StockCountLine> StockCountLines { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<NotificationRead> NotificationReads { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<SubscriptionPlan> SubscriptionPlans { get; }
    DbSet<TenantSubscription> TenantSubscriptions { get; }
    DbSet<IdempotencyRecord> IdempotencyRecords { get; }
    DbSet<EmailMessage> EmailMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Microsoft.EntityFrameworkCore.Infrastructure.DatabaseFacade Database { get; }
}
