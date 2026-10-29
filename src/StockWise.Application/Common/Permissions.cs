namespace StockWise.Application.Common;

/// <summary>Granular permission catalogue. Server-enforced; the frontend only mirrors these for UI gating.</summary>
public static class Permissions
{
    public const string ProductsRead = "Products.Read";
    public const string ProductsCreate = "Products.Create";
    public const string ProductsUpdate = "Products.Update";
    public const string ProductsArchive = "Products.Archive";
    public const string CatalogManage = "Catalog.Manage";            // categories + units
    public const string WarehousesRead = "Warehouses.Read";
    public const string WarehousesManage = "Warehouses.Manage";
    public const string InventoryRead = "Inventory.Read";
    public const string InventoryAdjust = "Inventory.Adjust";
    public const string InventoryTransfer = "Inventory.Transfer";
    public const string InventoryCount = "Inventory.Count";
    public const string InventoryCountApprove = "Inventory.CountApprove";
    public const string BatchesManage = "Batches.Manage";            // quarantine/release
    public const string SuppliersRead = "Suppliers.Read";
    public const string SuppliersManage = "Suppliers.Manage";
    public const string PurchaseOrdersRead = "PurchaseOrders.Read";
    public const string PurchaseOrdersCreate = "PurchaseOrders.Create";
    public const string PurchaseOrdersSubmit = "PurchaseOrders.Submit";
    public const string PurchaseOrdersApprove = "PurchaseOrders.Approve";
    public const string PurchaseOrdersCancel = "PurchaseOrders.Cancel";
    public const string ReceiptsCreate = "Receipts.Create";
    public const string ReportsRead = "Reports.Read";
    public const string NotificationsRead = "Notifications.Read";
    public const string UsersManage = "Users.Manage";
    public const string OrgSettingsManage = "OrgSettings.Manage";
    public const string AuditLogsRead = "AuditLogs.Read";

    public static readonly string[] All =
    [
        ProductsRead, ProductsCreate, ProductsUpdate, ProductsArchive, CatalogManage,
        WarehousesRead, WarehousesManage, InventoryRead, InventoryAdjust, InventoryTransfer,
        InventoryCount, InventoryCountApprove, BatchesManage, SuppliersRead, SuppliersManage,
        PurchaseOrdersRead, PurchaseOrdersCreate, PurchaseOrdersSubmit, PurchaseOrdersApprove,
        PurchaseOrdersCancel, ReceiptsCreate, ReportsRead, NotificationsRead, UsersManage,
        OrgSettingsManage, AuditLogsRead
    ];

    /// <summary>Role templates seeded for every new organization.</summary>
    public static readonly Dictionary<string, string[]> RoleTemplates = new()
    {
        ["Business Owner"] = All,
        ["Business Admin"] = All,
        ["Inventory Manager"] =
        [
            ProductsRead, ProductsCreate, ProductsUpdate, ProductsArchive, CatalogManage,
            WarehousesRead, WarehousesManage, InventoryRead, InventoryAdjust, InventoryTransfer,
            InventoryCount, InventoryCountApprove, BatchesManage, SuppliersRead, PurchaseOrdersRead,
            ReportsRead, NotificationsRead
        ],
        ["Procurement Officer"] =
        [
            ProductsRead, WarehousesRead, InventoryRead, SuppliersRead, SuppliersManage,
            PurchaseOrdersRead, PurchaseOrdersCreate, PurchaseOrdersSubmit, ReportsRead, NotificationsRead
        ],
        ["Approver"] =
        [
            ProductsRead, WarehousesRead, InventoryRead, SuppliersRead, PurchaseOrdersRead,
            PurchaseOrdersApprove, PurchaseOrdersCancel, ReportsRead, NotificationsRead
        ],
        ["Storekeeper"] =
        [
            ProductsRead, WarehousesRead, InventoryRead, InventoryAdjust, InventoryTransfer,
            InventoryCount, ReceiptsCreate, SuppliersRead, PurchaseOrdersRead, ReportsRead, NotificationsRead
        ],
        ["Sales / Read-Only"] =
        [
            ProductsRead, WarehousesRead, InventoryRead, SuppliersRead, PurchaseOrdersRead,
            ReportsRead, NotificationsRead
        ]
    };
}
