using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

public class StockTransfer : TenantEntity, ICreatable
{
    public string Number { get; set; } = default!;
    public Guid SourceWarehouseId { get; set; }
    public Guid DestinationWarehouseId { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Draft;
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public Guid? DispatchedBy { get; set; }
    public DateTime? DispatchedAt { get; set; }
    public Guid? ReceivedBy { get; set; }
    public DateTime? ReceivedAt { get; set; }

    public List<StockTransferLine> Lines { get; set; } = [];
}

public class StockTransferLine : Entity
{
    public Guid StockTransferId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BatchId { get; set; }
    public decimal Quantity { get; set; }
}

public class StockCount : TenantEntity, ICreatable
{
    public string Number { get; set; } = default!;
    public Guid WarehouseId { get; set; }
    public StockCountStatus Status { get; set; } = StockCountStatus.Draft;
    public bool BlindCount { get; set; }
    public string? Notes { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public Guid? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public Guid? PostedBy { get; set; }
    public DateTime? PostedAt { get; set; }

    public List<StockCountLine> Lines { get; set; } = [];
}

public class StockCountLine : Entity
{
    public Guid StockCountId { get; set; }
    public Guid ProductId { get; set; }
    public Guid? BatchId { get; set; }
    /// <summary>Snapshotted balance when the count was created.</summary>
    public decimal ExpectedQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public string? Reason { get; set; }
}

public class Notification : TenantEntity
{
    public string Type { get; set; } = default!;        // e.g. "LowStock", "OutOfStock", "ExpiringSoon", "Expired", "PurchaseOrderApproval", "OverdueDelivery"
    public string Title { get; set; } = default!;
    public string Message { get; set; } = default!;
    public NotificationSeverity Severity { get; set; }
    /// <summary>Stable key used to deduplicate recurring alerts, e.g. "LowStock:{productId}". Unique per tenant.</summary>
    public string DedupKey { get; set; } = default!;
    public string? Link { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAtUtc { get; set; }        // set when the condition no longer holds
}

public class NotificationRead : Entity
{
    public Guid NotificationId { get; set; }
    public Guid UserId { get; set; }
    public DateTime ReadAtUtc { get; set; } = DateTime.UtcNow;
}

public class AuditLog : Entity
{
    public Guid? TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string ActorEmail { get; set; } = default!;
    public string Action { get; set; } = default!;      // e.g. "Product.Created", "PurchaseOrder.Approved"
    public string EntityType { get; set; } = default!;
    public string? EntityId { get; set; }
    public string? Details { get; set; }                // safe JSON summary (no secrets)
    public string? IpAddress { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class SubscriptionPlan : Entity
{
    public string Code { get; set; } = default!;
    public string Name { get; set; } = default!;
    public int MaxUsers { get; set; }
    public int MaxWarehouses { get; set; }
    public int MaxActiveProducts { get; set; }
    public decimal PriceMonthly { get; set; }
    public bool IsActive { get; set; } = true;
}

public class TenantSubscription : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid PlanId { get; set; }
    public string Status { get; set; } = "Active";      // plan assignment only; no billing integration in MVP
    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CurrentPeriodEndUtc { get; set; }
}

public class IdempotencyRecord : TenantEntity
{
    public string Key { get; set; } = default!;
    public string Endpoint { get; set; } = default!;
    public string? ResponseSnapshot { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Development-mode email outbox: emails are recorded here (and logged) instead of being sent.</summary>
public class EmailMessage : Entity
{
    public string To { get; set; } = default!;
    public string Subject { get; set; } = default!;
    public string Body { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SentAtUtc { get; set; }
}
