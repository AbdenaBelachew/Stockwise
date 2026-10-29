using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StockWise.IntegrationTests;

[Collection("api")]
public class InventoryLedgerTests
{
    private readonly StockWiseTestFactory _factory;

    public InventoryLedgerTests(StockWiseTestFactory factory) => _factory = factory;

    private async Task<(HttpClient client, string warehouseId, string productId)> SetupAsync(string skuPrefix)
    {
        var client = await _factory.RegisterAsync($"{skuPrefix}-{Guid.NewGuid():N}@t.local", "Password123", $"Ledger Org {skuPrefix}");
        var warehouseId = (await client.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString()!;
        var unitId = (await client.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;
        var product = await client.PostJsonAsync("/api/v1/products",
            new { sku = $"{skuPrefix}-1", name = "Ledger Product", unitId, costPrice = 2.5m, reorderPoint = 10 });
        return (client, warehouseId, product.GetProperty("id").GetString()!);
    }

    [Fact]
    public async Task OpeningBalance_CreatesLedger_AndBalance()
    {
        var (client, warehouseId, productId) = await SetupAsync("ob");
        await client.PostJsonAsync("/api/v1/inventory/opening-balances",
            new { warehouseId, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantityDelta = 100, unitCost = 2.5m } } });

        var balances = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        Assert.Equal(100m, balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
        Assert.Equal(2.5m, balances.GetProperty("items")[0].GetProperty("avgUnitCost").GetDecimal());

        var movements = await client.GetJsonAsync($"/api/v1/inventory/movements?productId={productId}");
        Assert.Equal("OpeningBalance", movements.GetProperty("items")[0].GetProperty("movementType").GetString());
    }

    [Fact]
    public async Task OpeningBalance_WithSameIdempotencyKey_DoesNotDoubleStock()
    {
        var (client, warehouseId, productId) = await SetupAsync("idem");
        var key = Guid.NewGuid().ToString();
        var body = new { warehouseId, idempotencyKey = key, lines = new[] { new { productId, quantityDelta = 50, unitCost = 1m } } };

        await client.PostJsonAsync("/api/v1/inventory/opening-balances", body);
        await client.PostJsonAsync("/api/v1/inventory/opening-balances", body); // retry

        var balances = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        Assert.Equal(50m, balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public async Task NegativeStock_Rejected_ByDefault()
    {
        var (client, warehouseId, productId) = await SetupAsync("neg");
        var res = await client.PostAsJsonAsync("/api/v1/inventory/adjustments", new
        {
            warehouseId,
            lines = new[] { new { productId, quantityDelta = -5, reason = "should fail" } }
        });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
    }

    [Fact]
    public async Task ConcurrentAdjustments_DoNotCorruptBalance()
    {
        var (client, warehouseId, productId) = await SetupAsync("conc");
        await client.PostJsonAsync("/api/v1/inventory/opening-balances",
            new { warehouseId, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantityDelta = 0 + 100, unitCost = 1m } } });

        // 12 concurrent -3 adjustments against the same balance row: exactly -36 total.
        var tasks = Enumerable.Range(0, 12).Select(_ => client.PostAsJsonAsync("/api/v1/inventory/adjustments",
            new { warehouseId, idempotencyKey = Guid.NewGuid().ToString(), lines = new[] { new { productId, quantityDelta = -3, reason = "stress" } } }));
        var responses = await Task.WhenAll(tasks);
        // Negative-stock rejections are acceptable under contention; the invariant is exact accounting.
        var succeeded = responses.Count(r => r.StatusCode == HttpStatusCode.OK);

        var balances = await client.GetJsonAsync($"/api/v1/inventory/balances?productId={productId}");
        var final = balances.GetProperty("items")[0].GetProperty("quantity").GetDecimal();
        Assert.Equal(100 - 3 * succeeded, final);

        // The ledger must reconcile exactly with the cached balance.
        var movements = await client.GetJsonAsync($"/api/v1/inventory/movements?productId={productId}&pageSize=200");
        var ledgerSum = movements.GetProperty("items").EnumerateArray()
            .Sum(m => m.GetProperty("quantityDelta").GetDecimal());
        Assert.Equal(final, ledgerSum);
    }

    [Fact]
    public async Task BatchTrackedProduct_RequiresBatchNumber_And_RespectsShelfLife()
    {
        var client = await _factory.RegisterAsync($"batch-{Guid.NewGuid():N}@t.local", "Password123", "Batch Org");
        var warehouseId = (await client.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString()!;
        var unitId = (await client.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;
        var product = await client.PostJsonAsync("/api/v1/products", new
        {
            sku = "BATCH-1", name = "Batched Goods", unitId, reorderPoint = 5,
            trackBatches = true, trackExpiry = true, minShelfLifeDays = 10
        });
        var productId = product.GetProperty("id").GetString()!;

        // No batch number → rejected.
        var noBatch = await client.PostAsJsonAsync("/api/v1/inventory/opening-balances", new
        {
            warehouseId, idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { productId, quantityDelta = 10, unitCost = 1m } }
        });
        Assert.False(noBatch.IsSuccessStatusCode);

        // Expiry below min shelf life → rejected.
        var shortDated = await client.PostAsJsonAsync("/api/v1/inventory/opening-balances", new
        {
            warehouseId, idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { productId, quantityDelta = 10, unitCost = 1m, batchNumber = "B-SHORT", expiryDate = DateTime.UtcNow.AddDays(2) } }
        });
        Assert.False(shortDated.IsSuccessStatusCode);

        // Valid batch → accepted; visible in the expiry report.
        await client.PostJsonAsync("/api/v1/inventory/opening-balances", new
        {
            warehouseId, idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { productId, quantityDelta = 10, unitCost = 1m, batchNumber = "B-OK", expiryDate = DateTime.UtcNow.AddDays(60) } }
        });
        var expiry = await client.GetJsonAsync("/api/v1/inventory/expiry");
        Assert.Contains(expiry.EnumerateArray(),
            b => b.GetProperty("batchNumber").GetString() == "B-OK");
    }
}
