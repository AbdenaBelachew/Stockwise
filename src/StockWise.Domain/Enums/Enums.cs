namespace StockWise.Domain.Enums;

public enum PurchaseOrderStatus
{
    Draft = 0,
    PendingApproval = 1,
    Approved = 2,
    Ordered = 3,
    PartiallyReceived = 4,
    Received = 5,
    Rejected = 6,
    Cancelled = 7
}

public enum MovementType
{
    OpeningBalance = 0,
    GoodsReceipt = 1,
    StockIssue = 2,
    Sale = 3,
    TransferOut = 4,
    TransferIn = 5,
    Adjustment = 6,
    CustomerReturn = 7,
    SupplierReturn = 8,
    Reversal = 9,
    StockCountVariance = 10
}

public enum TransferStatus
{
    Draft = 0,
    InTransit = 1,
    Received = 2,
    Cancelled = 3
}

public enum StockCountStatus
{
    Draft = 0,
    Submitted = 1,
    Approved = 2,
    Posted = 3,
    Cancelled = 4
}

public enum BatchStatus
{
    Active = 0,
    Quarantined = 1,
    Expired = 2,
    Depleted = 3
}

public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Revoked = 2,
    Expired = 3
}

public enum MembershipStatus
{
    Active = 0,
    Disabled = 1
}

public enum OrganizationStatus
{
    Active = 0,
    Suspended = 1
}

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Critical = 2
}

public enum ApprovalAction
{
    Submitted = 0,
    Approved = 1,
    Rejected = 2,
    Cancelled = 3,
    Ordered = 4
}

public enum CostMethod
{
    WeightedAverage = 0
}

public enum ProductStatus
{
    Active = 0,
    Archived = 1
}
