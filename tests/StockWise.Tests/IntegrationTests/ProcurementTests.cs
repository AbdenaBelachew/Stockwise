using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace StockWise.IntegrationTests;

[Collection("api")]
public class ProcurementTests
{
    private readonly StockWiseTestFactory _factory;

    public ProcurementTests(StockWiseTestFactory factory) => _factory = factory;

    private record Setup(HttpClient Buyer, HttpClient Approver, string WarehouseId, string SupplierId, string ProductId);

    private async Task<Setup> SetupAsync()
    {
        // Buyer creates everything; an approver user (same org, Approver role via owner) approves.
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var buyer = await _factory.RegisterAsync($"buyer-{suffix}@t.local", "Password123", $"Procurement {suffix}");
        var approver = await _factory.RegisterAsync($"approver-{suffix}@t.local", "Password123", $"Procurement2 {suffix}");

        // Add approver to buyer's organization via invitation accept flow (needs the raw token).
        var invite = await buyer.PostJsonAsync("/api/v1/organizations/current/invitations",
            new { email = $"approver-{suffix}@t.local", roleId = (await buyer.GetJsonAsync("/api/v1/organizations/current/roles"))[0].GetProperty("id").GetString() });
        var token = invite.GetProperty("acceptToken").GetString();
        await approver.PostJsonAsync("/api/v1/invitations/accept",
            new { token, fullName = "Approver Two", password = "Password123" });
        // Re-login so the approver client carries a token for the buyer's organization.
        approver = await _factory.LoginAsync($"approver-{suffix}@t.local", "Password123");
        var me = await approver.GetJsonAsync("/api/v1/auth/me");
        var buyerOrg = me.GetProperty("organizations").EnumerateArray()
            .First(o => o.GetProperty("name").GetString() == $"Procurement {suffix}")
            .GetProperty("id").GetString()!;
        approver = await _factory.LoginAsync($"approver-{suffix}@t.local", "Password123");
        var switched = await approver.PostJsonAsync("/api/v1/auth/switch-organization", new { organizationId = buyerOrg });
        approver.DefaultRequestHeaders.Authorization = new("Bearer", switched.GetProperty("accessToken").GetString());

        var warehouseId = (await buyer.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString()!;
        var unitId = (await buyer.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;
        var supplier = await buyer.PostJsonAsync("/api/v1/suppliers",
            new { code = $"SUP-{suffix}", name = "Test Supplier", paymentTermsDays = 30 });
        var product = await buyer.PostJsonAsync("/api/v1/products",
            new { sku = $"PO-{suffix}", name = "Purchased Item", unitId, costPrice = 4m, reorderPoint = 20 });

        return new Setup(buyer, approver, warehouseId, supplier.GetProperty("id").GetString()!, product.GetProperty("id").GetString()!);
    }

    private static object PoBody(Setup s, decimal qty = 100, decimal cost = 4m) => new
    {
        supplierId = s.SupplierId,
        warehouseId = s.WarehouseId,
        expectedDeliveryDate = (DateTime?)DateTime.UtcNow.AddDays(7),
        lines = new[] { new { productId = s.ProductId, quantity = qty, unitCost = cost, discountPercent = 0m, taxPercent = 0m } }
    };

    [Fact]
    public async Task FullLifecycle_Submit_Approve_Receive_Partial_ThenComplete()
    {
        var s = await SetupAsync();

        var po = await s.Buyer.PostJsonAsync("/api/v1/purchase-orders", PoBody(s));
        var poId = po.GetProperty("id").GetString()!;

        // Submit
        var submitted = await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/submit", new { });
        Assert.Equal("PendingApproval", submitted.GetProperty("status").GetString());

        // Approval separation of duties: the creator cannot approve.
        var selfApprove = await s.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{poId}/approve", new { });
        Assert.Equal(HttpStatusCode.Forbidden, selfApprove.StatusCode);

        // A different user approves.
        var approved = await s.Approver.PostJsonAsync($"/api/v1/purchase-orders/{poId}/approve", new { });
        Assert.Equal("Approved", approved.GetProperty("status").GetString());

        // Receiving 60 of 100 → PartiallyReceived; balance only changes via the receipt.
        var poDetail = await s.Buyer.GetJsonAsync($"/api/v1/purchase-orders/{poId}");
        var lineId = poDetail.GetProperty("lines")[0].GetProperty("id").GetString()!;

        var receipt = await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", new
        {
            warehouseId = s.WarehouseId,
            idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { purchaseOrderLineId = lineId, quantityAccepted = 60m, quantityRejected = 0m } }
        });
        Assert.Equal("PartiallyReceived", receipt.GetProperty("purchaseOrderStatus").GetString());

        var balances = await s.Buyer.GetJsonAsync($"/api/v1/inventory/balances?productId={s.ProductId}");
        Assert.Equal(60m, balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal());

        // Receiving the remaining 40 → Received.
        var receipt2 = await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", new
        {
            warehouseId = s.WarehouseId,
            idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { purchaseOrderLineId = lineId, quantityAccepted = 40m, quantityRejected = 0m } }
        });
        Assert.Equal("Received", receipt2.GetProperty("purchaseOrderStatus").GetString());
    }

    [Fact]
    public async Task DuplicateReceipt_WithSameIdempotencyKey_DoesNotReceiveTwice()
    {
        var s = await SetupAsync();
        var po = await s.Buyer.PostJsonAsync("/api/v1/purchase-orders", PoBody(s));
        var poId = po.GetProperty("id").GetString()!;
        await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/submit", new { });
        await s.Approver.PostJsonAsync($"/api/v1/purchase-orders/{poId}/approve", new { });
        var lineId = (await s.Buyer.GetJsonAsync($"/api/v1/purchase-orders/{poId}"))
            .GetProperty("lines")[0].GetProperty("id").GetString()!;

        var key = Guid.NewGuid().ToString();
        var receiptBody = new
        {
            warehouseId = s.WarehouseId,
            idempotencyKey = key,
            lines = new[] { new { purchaseOrderLineId = lineId, quantityAccepted = 100m, quantityRejected = 0m } }
        };
        await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", receiptBody);
        await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", receiptBody); // network retry simulation

        var balances = await s.Buyer.GetJsonAsync($"/api/v1/inventory/balances?productId={s.ProductId}");
        Assert.Equal(100m, balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task OverReceiving_IsRejected_WhenPolicyDisabled()
    {
        var s = await SetupAsync();
        var po = await s.Buyer.PostJsonAsync("/api/v1/purchase-orders", PoBody(s, qty: 50));
        var poId = po.GetProperty("id").GetString()!;
        await s.Buyer.PostJsonAsync($"/api/v1/purchase-orders/{poId}/submit", new { });
        await s.Approver.PostJsonAsync($"/api/v1/purchase-orders/{poId}/approve", new { });
        var lineId = (await s.Buyer.GetJsonAsync($"/api/v1/purchase-orders/{poId}"))
            .GetProperty("lines")[0].GetProperty("id").GetString()!;

        var res = await s.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", new
        {
            warehouseId = s.WarehouseId,
            idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { purchaseOrderLineId = lineId, quantityAccepted = 60m, quantityRejected = 0m } }
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Receiving_BeforeApproval_IsRejected()
    {
        var s = await SetupAsync();
        var po = await s.Buyer.PostJsonAsync("/api/v1/purchase-orders", PoBody(s));
        var poId = po.GetProperty("id").GetString()!;
        var lineId = (await s.Buyer.GetJsonAsync($"/api/v1/purchase-orders/{poId}"))
            .GetProperty("lines")[0].GetProperty("id").GetString()!;

        var res = await s.Buyer.PostAsJsonAsync($"/api/v1/purchase-orders/{poId}/receipts", new
        {
            warehouseId = s.WarehouseId,
            idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { purchaseOrderLineId = lineId, quantityAccepted = 10m, quantityRejected = 0m } }
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task Transfers_MoveStock_WithControlledTransitions()
    {
        var client = await _factory.RegisterAsync($"trf-{Guid.NewGuid():N}@t.local", "Password123", "Transfer Org");
        var warehouses = await client.GetJsonAsync("/api/v1/warehouses");
        var source = warehouses[0].GetProperty("id").GetString()!;
        var unitId = (await client.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;
        var product = await client.PostJsonAsync("/api/v1/products",
            new { sku = "TRF-1", name = "Transfer Item", unitId, costPrice = 3m, reorderPoint = 0 });
        var productId = product.GetProperty("id").GetString()!;

        // Second warehouse
        var dest = (await client.PostJsonAsync("/api/v1/warehouses",
            new { name = "Second Warehouse", code = "SEC" })).GetProperty("id").GetString()!;

        await client.PostJsonAsync("/api/v1/inventory/opening-balances",
            new { warehouseId = source, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantityDelta = 40, unitCost = 3m } } });

        // Same-warehouse transfer rejected.
        var same = await client.PostAsJsonAsync("/api/v1/inventory/transfers",
            new { sourceWarehouseId = source, destinationWarehouseId = source, lines = new[] { new { productId, quantity = 5 } } });
        Assert.False(same.IsSuccessStatusCode);

        var transfer = await client.PostJsonAsync("/api/v1/inventory/transfers",
            new { sourceWarehouseId = source, destinationWarehouseId = dest, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantity = 15 } } });
        var transferId = transfer.GetProperty("id").GetString()!;

        await client.PostJsonAsync($"/api/v1/inventory/transfers/{transferId}/dispatch", new { });
        // Source deducted while in transit.
        var during = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        Assert.Equal(25m, during.GetProperty("items").EnumerateArray().Where(b => b.GetProperty("warehouseId").GetString() == source).First().GetProperty("quantity").GetDecimal());

        await client.PostJsonAsync($"/api/v1/inventory/transfers/{transferId}/receive", new { });
        var after = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        Assert.Equal(40m, after.GetProperty("items").EnumerateArray().Sum(b => b.GetProperty("quantity").GetDecimal()));

        // Re-receiving is rejected (transition guard).
        var again = await client.PostAsJsonAsync($"/api/v1/inventory/transfers/{transferId}/receive", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task StockCount_PostsVariances_OnceOnly()
    {
        var client = await _factory.RegisterAsync($"cnt-{Guid.NewGuid():N}@t.local", "Password123", "Count Org");
        var warehouseId = (await client.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString()!;
        var unitId = (await client.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;
        var product = await client.PostJsonAsync("/api/v1/products",
            new { sku = "CNT-1", name = "Counted Item", unitId, costPrice = 1m, reorderPoint = 0 });
        var productId = product.GetProperty("id").GetString()!;

        await client.PostJsonAsync("/api/v1/inventory/opening-balances",
            new { warehouseId, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantityDelta = 100, unitCost = 1m } } });

        var count = await client.PostJsonAsync("/api/v1/inventory/stock-counts",
            new { warehouseId, blindCount = false, productIds = new[] { new { productId } } });
        var countId = count.GetProperty("id").GetString()!;

        // Counted 96 → variance -4.
        await client.PostJsonAsync($"/api/v1/inventory/stock-counts/{countId}/count",
            new { lines = new[] { new { productId, batchId = (string?)null, countedQuantity = 96m, reason = "Cycle count" } } });
        await client.PostJsonAsync($"/api/v1/inventory/stock-counts/{countId}/submit", new { });
        await client.PostJsonAsync($"/api/v1/inventory/stock-counts/{countId}/approve", new { });
        await client.PostJsonAsync($"/api/v1/inventory/stock-counts/{countId}/post", new { });

        var balances = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        Assert.Equal(96m, balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal());

        // Posting twice is rejected.
        var again = await client.PostAsJsonAsync($"/api/v1/inventory/stock-counts/{countId}/post", new { });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }
}
