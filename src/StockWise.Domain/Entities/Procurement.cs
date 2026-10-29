using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

public class PurchaseOrder : TenantEntity, ICreatable
{
    public string Number { get; set; } = default!;
    public Guid SupplierId { get; set; }
    public Guid WarehouseId { get; set; }
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public string CurrencyCode { get; set; } = default!;
    public DateTime? ExpectedDeliveryDate { get; set; }
    public string? Notes { get; set; }
    public decimal SubTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal Total { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = [];
    public List<PurchaseApproval> Approvals { get; set; } = [];
}

public class PurchaseOrderLine : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public Guid ProductId { get; set; }
    public decimal OrderedQuantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }
    /// <summary>Computed line total after discount and tax: qty * unitCost * (1 - disc) * (1 + tax).</summary>
    public decimal LineTotal { get; set; }
    public string? Notes { get; set; }
}

public class PurchaseApproval : Entity
{
    public Guid PurchaseOrderId { get; set; }
    public ApprovalAction Action { get; set; }
    public Guid ActorUserId { get; set; }
    public string? Reason { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
}

public class GoodsReceipt : TenantEntity, ICreatable
{
    public string Number { get; set; } = default!;
    public Guid PurchaseOrderId { get; set; }
    public Guid WarehouseId { get; set; }
    public string? Notes { get; set; }
    /// <summary>Client-supplied idempotency key; unique per tenant to prevent duplicate receiving.</summary>
    public string? IdempotencyKey { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public class GoodsReceiptLine : Entity
{
    public Guid GoodsReceiptId { get; set; }
    public Guid PurchaseOrderLineId { get; set; }
    public Guid ProductId { get; set; }
    public decimal QuantityAccepted { get; set; }
    public decimal QuantityRejected { get; set; }
    public string? RejectReason { get; set; }
    public Guid? BatchId { get; set; }
    public string? BatchNumber { get; set; }
    public DateTime? ExpiryDate { get; set; }
}
