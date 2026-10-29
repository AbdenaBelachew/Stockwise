using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockWise.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFilteringIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Full-text-ish search support: the list endpoints search with LIKE '%term%',
            // which plain btree indexes cannot serve. pg_trgm GIN expression indexes do.
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS pg_trgm;

                CREATE INDEX IF NOT EXISTS ix_products_name_trgm
                    ON products USING gin (lower(name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_products_sku_trgm
                    ON products USING gin (lower(sku) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_suppliers_name_trgm
                    ON suppliers USING gin (lower(name) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_inventory_movements_reference_trgm
                    ON inventory_movements USING gin (lower(reference_number) gin_trgm_ops);
                CREATE INDEX IF NOT EXISTS ix_purchase_orders_number_trgm
                    ON purchase_orders USING gin (lower(number) gin_trgm_ops);
            """);
            migrationBuilder.DropIndex(
                name: "ix_notifications_tenant_id_created_at_utc",
                table: "notifications");

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_id_is_active",
                table: "suppliers",
                columns: new[] { "tenant_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_transfers_tenant_id_created_at",
                table: "stock_transfers",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_tenant_id_created_at",
                table: "stock_counts",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_tenant_id_created_at",
                table: "purchase_orders",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_default_supplier_id",
                table: "products",
                columns: new[] { "tenant_id", "default_supplier_id" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_id_reorder_point",
                table: "products",
                columns: new[] { "tenant_id", "reorder_point" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unresolved_by_date",
                table: "notifications",
                columns: new[] { "tenant_id", "created_at_utc" },
                filter: "resolved_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id_movement_type",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "movement_type" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_tenant_id_warehouse_id_created_at_utc",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "warehouse_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_balances_tenant_id_warehouse_id",
                table: "inventory_balances",
                columns: new[] { "tenant_id", "warehouse_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_tenant_id_created_at",
                table: "goods_receipts",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_batches_tenant_id_warehouse_id_expiry_date",
                table: "batches",
                columns: new[] { "tenant_id", "warehouse_id", "expiry_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS ix_purchase_orders_number_trgm;
                DROP INDEX IF EXISTS ix_inventory_movements_reference_trgm;
                DROP INDEX IF EXISTS ix_suppliers_name_trgm;
                DROP INDEX IF EXISTS ix_products_sku_trgm;
                DROP INDEX IF EXISTS ix_products_name_trgm;
            """);
            migrationBuilder.DropIndex(
                name: "ix_suppliers_tenant_id_is_active",
                table: "suppliers");

            migrationBuilder.DropIndex(
                name: "ix_stock_transfers_tenant_id_created_at",
                table: "stock_transfers");

            migrationBuilder.DropIndex(
                name: "ix_stock_counts_tenant_id_created_at",
                table: "stock_counts");

            migrationBuilder.DropIndex(
                name: "ix_purchase_orders_tenant_id_created_at",
                table: "purchase_orders");

            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_default_supplier_id",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_tenant_id_reorder_point",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_notifications_unresolved_by_date",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_tenant_id_movement_type",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_movements_tenant_id_warehouse_id_created_at_utc",
                table: "inventory_movements");

            migrationBuilder.DropIndex(
                name: "ix_inventory_balances_tenant_id_warehouse_id",
                table: "inventory_balances");

            migrationBuilder.DropIndex(
                name: "ix_goods_receipts_tenant_id_created_at",
                table: "goods_receipts");

            migrationBuilder.DropIndex(
                name: "ix_batches_tenant_id_warehouse_id_expiry_date",
                table: "batches");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_tenant_id_created_at_utc",
                table: "notifications",
                columns: new[] { "tenant_id", "created_at_utc" });
        }
    }
}
