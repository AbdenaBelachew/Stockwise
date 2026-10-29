using StockWise.Domain.Common;
using StockWise.Domain.Enums;

namespace StockWise.Domain.Entities;

/// <summary>
/// The append-only ledger of every stock change. This is the source of truth for inventory.
/// Posted movements are immutable; corrections are compensating movements.
/// QuantityDelta is signed: positive adds stock, negative removes it.
/// </summary>
public class InventoryMovement : TenantEntity
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public Guid? BatchId { get; set; }
    public MovementType MovementType { get; set; }
    public decimal QuantityDelta { get; set; }
    /// <summary>Unit cost captured at posting time (IN movements set/update average cost; OUT movements carry the average out).</summary>
    public decimal? UnitCost { get; set; }
    public string? ReferenceType { get; set; }     // e.g. "PurchaseOrder", "GoodsReceipt", "StockTransfer", "StockCount", "Adjustment"
    public Guid? ReferenceId { get; set; }
    public string? ReferenceNumber { get; set; }
    public string? Reason { get; set; }
    public Guid UserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Concurrency-protected per (product, warehouse) balance projection.
/// A cache of the ledger: recalculable from InventoryMovement, updated atomically
/// with each posting under a row lock so concurrent operations cannot corrupt stock.
/// </summary>
public class InventoryBalance : TenantEntity
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public decimal Quantity { get; set; }
    /// <summary>Weighted-average unit cost at cost method = WeightedAverage.</summary>
    public decimal AvgUnitCost { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>A batch/lot of a batch-tracked product in a specific warehouse.</summary>
public class Batch : TenantEntity
{
    public Guid ProductId { get; set; }
    public Guid WarehouseId { get; set; }
    public string BatchNumber { get; set; } = default!;
    public DateTime? ExpiryDate { get; set; }          // date only (UTC midnight); expiry policy: expired when current local date > ExpiryDate
    public Guid? SupplierId { get; set; }
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal InitialQuantity { get; set; }
    public decimal QuantityRemaining { get; set; }
    public decimal UnitCost { get; set; }
    public BatchStatus Status { get; set; } = BatchStatus.Active;
}

/// <summary>Per-tenant monotonic number sequences for human-readable document numbers.</summary>
public class NumberSequence : TenantEntity
{
    public string SequenceType { get; set; } = default!;   // PURCHASE_ORDER | GOODS_RECEIPT | TRANSFER | STOCK_COUNT
    public int LastNumber { get; set; }
}
