using System.Net;
using Xunit;

namespace StockWise.IntegrationTests;

[Collection("api")]
public class ReportsChartsTests
{
    private readonly StockWiseTestFactory _factory;

    public ReportsChartsTests(StockWiseTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Charts_Aggregate_Value_ByCategory_AndWarehouse()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var client = await _factory.RegisterAsync($"charts-{suffix}@t.local", "Password123", $"Charts Org {suffix}");
        var warehouseId = (await client.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString()!;
        var unitId = (await client.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString()!;

        var category = await client.PostJsonAsync("/api/v1/categories", new { name = $"Cat {suffix}" });
        var product = await client.PostJsonAsync("/api/v1/products", new
        {
            sku = $"CHT-{suffix}", name = "Charted Item", unitId,
            categoryId = category.GetProperty("id").GetString(), costPrice = 10m, reorderPoint = 0
        });
        var productId = product.GetProperty("id").GetString()!;

        await client.PostJsonAsync("/api/v1/inventory/opening-balances", new
        {
            warehouseId, idempotencyKey = Guid.NewGuid().ToString(),
            lines = new[] { new { productId, quantityDelta = 20, unitCost = 10m } }
        });

        var charts = await client.GetJsonAsync("/api/v1/reports/charts");

        var cat = charts.GetProperty("valueByCategory").EnumerateArray()
            .First(c => c.GetProperty("category").GetString() == $"Cat {suffix}");
        Assert.Equal(200m, cat.GetProperty("value").GetDecimal());
        Assert.Equal(20m, cat.GetProperty("quantity").GetDecimal());

        Assert.Contains(charts.GetProperty("valueByWarehouse").EnumerateArray(),
            w => w.GetProperty("value").GetDecimal() == 200m);

        Assert.Equal(1, charts.GetProperty("stockHealth").GetProperty("inStock").GetInt32());
        Assert.Equal(0, charts.GetProperty("stockHealth").GetProperty("outOfStock").GetInt32());
    }

    [Fact]
    public async Task Charts_RequireReportsPermission()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var owner = await _factory.RegisterAsync($"own-{suffix}@t.local", "Password123", $"Charts A {suffix}");
        var invited = await _factory.RegisterAsync($"inv-{suffix}@t.local", "Password123", $"Charts B {suffix}");

        // Invite the second user as Storekeeper (no Reports.Read).
        var roles = await owner.GetJsonAsync("/api/v1/organizations/current/roles");
        var storekeeperRole = roles.EnumerateArray().First(r => r.GetProperty("name").GetString() == "Storekeeper");
        var invite = await owner.PostJsonAsync("/api/v1/organizations/current/invitations",
            new { email = $"inv-{suffix}@t.local", roleId = storekeeperRole.GetProperty("id").GetString() });
        await invited.PostJsonAsync("/api/v1/invitations/accept",
            new { token = invite.GetProperty("acceptToken").GetString(), fullName = "Invited", password = "Password123" });

        var res = await invited.GetAsync("/api/v1/reports/charts");
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
