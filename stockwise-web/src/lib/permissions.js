// Mirrors backend StockWise.Application.Common.Permissions. Used only for UI
// gating (menus/buttons) — the server always re-checks.

export const P = {
  ProductsRead: 'Products.Read',
  ProductsCreate: 'Products.Create',
  ProductsUpdate: 'Products.Update',
  ProductsArchive: 'Products.Archive',
  CatalogManage: 'Catalog.Manage',
  WarehousesRead: 'Warehouses.Read',
  WarehousesManage: 'Warehouses.Manage',
  InventoryRead: 'Inventory.Read',
  InventoryAdjust: 'Inventory.Adjust',
  InventoryTransfer: 'Inventory.Transfer',
  InventoryCount: 'Inventory.Count',
  InventoryCountApprove: 'Inventory.CountApprove',
  BatchesManage: 'Batches.Manage',
  SuppliersRead: 'Suppliers.Read',
  SuppliersManage: 'Suppliers.Manage',
  PurchaseOrdersRead: 'PurchaseOrders.Read',
  PurchaseOrdersCreate: 'PurchaseOrders.Create',
  PurchaseOrdersSubmit: 'PurchaseOrders.Submit',
  PurchaseOrdersApprove: 'PurchaseOrders.Approve',
  PurchaseOrdersCancel: 'PurchaseOrders.Cancel',
  ReceiptsCreate: 'Receipts.Create',
  ReportsRead: 'Reports.Read',
  NotificationsRead: 'Notifications.Read',
  UsersManage: 'Users.Manage',
  OrgSettingsManage: 'OrgSettings.Manage',
  AuditLogsRead: 'AuditLogs.Read'
}
