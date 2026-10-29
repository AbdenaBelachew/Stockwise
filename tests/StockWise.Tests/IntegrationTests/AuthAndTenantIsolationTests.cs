using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace StockWise.IntegrationTests;

[Collection("api")]
public class AuthAndTenantIsolationTests
{
    private readonly StockWiseTestFactory _factory;

    public AuthAndTenantIsolationTests(StockWiseTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_Login_And_Me_Work()
    {
        var email = $"owner-{Guid.NewGuid():N}@t.local";
        var client = await _factory.RegisterAsync(email, "Password123", "Isolation Org A");

        var me = await client.GetJsonAsync("/api/v1/auth/me");
        Assert.Equal(email, me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Isolation Org A", me.GetProperty("activeOrganizationName").GetString());
        Assert.True(me.GetProperty("permissions").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Login_WithWrongPassword_Is401()
    {
        var email = $"wrongpw-{Guid.NewGuid():N}@t.local";
        await _factory.RegisterAsync(email, "Password123", "WrongPw Org");

        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "DefinitelyWrong1" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_DoesNotRevealUnknownEmails()
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "no-such-user@t.local", password = "Whatever123" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Lockout_AfterRepeatedFailures()
    {
        var email = $"lockout-{Guid.NewGuid():N}@t.local";
        await _factory.RegisterAsync(email, "Password123", "Lockout Org");

        var client = _factory.CreateClient();
        for (var i = 0; i < 5; i++)
            await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Bad1" + new string('x', i) });

        var res = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "Password123" });
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
    }

    [Fact]
    public async Task TenantA_CannotAccess_TenantB_Resources()
    {
        var userA = await _factory.RegisterAsync($"a-{Guid.NewGuid():N}@t.local", "Password123", "Tenant A");
        var userB = await _factory.RegisterAsync($"b-{Guid.NewGuid():N}@t.local", "Password123", "Tenant B");

        // A creates a product.
        var unitId = (await userA.GetJsonAsync("/api/v1/units"))[0].GetProperty("id").GetString();
        var product = await userA.PostJsonAsync("/api/v1/products",
            new { sku = "TENANT-A-1", name = "Secret Product A", unitId, reorderPoint = 5 });
        var productId = product.GetProperty("id").GetString();

        // B cannot read A's product or adjust its stock.
        var readRes = await userB.GetAsync($"/api/v1/products/{productId}");
        Assert.Equal(HttpStatusCode.NotFound, readRes.StatusCode); // hidden by tenant scope

        var adjustRes = await userB.PostAsJsonAsync("/api/v1/inventory/adjustments", new
        {
            warehouseId = (await userB.GetJsonAsync("/api/v1/warehouses"))[0].GetProperty("id").GetString(),
            lines = new[] { new { productId, quantityDelta = 10, reason = "attack" } }
        });
        Assert.Equal(HttpStatusCode.NotFound, adjustRes.StatusCode); // product not visible in B's tenant
    }

    [Fact]
    public async Task User_InMultipleOrgs_CanSwitch_And_LoseAccessOnWrongOrg()
    {
        // One user owns two organizations.
        var client = await _factory.RegisterAsync($"multi-{Guid.NewGuid():N}@t.local", "Password123", "Multi Org 1");
        var org2 = await client.PostJsonAsync("/api/v1/organizations",
            new { name = "Multi Org 2", country = "Germany", timeZoneId = "UTC", currencyCode = "USD", dateFormat = "yyyy-MM-dd" });
        var org2Id = org2.GetProperty("id").GetString();

        var switched = await client.PostJsonAsync("/api/v1/auth/switch-organization", new { organizationId = org2Id });
        Assert.Equal("Multi Org 2", switched.GetProperty("organizationName").GetString());

        // Unauthenticated request is rejected.
        var anon = _factory.CreateClient();
        var res = await anon.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task ExpiredOrTamperedToken_IsRejected()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", "not.a.jwt");
        var res = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
