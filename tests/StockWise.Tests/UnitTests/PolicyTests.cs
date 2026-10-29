using StockWise.Application.Common;
using StockWise.Application.Features.Inventory;
using Xunit;

namespace StockWise.UnitTests;

public class ExpiryPolicyTests
{
    [Theory]
    [InlineData(-1, "expired")]
    [InlineData(-30, "expired")]
    [InlineData(0, "critical")]     // last valid day: expires today, not yet expired
    [InlineData(7, "critical")]
    [InlineData(8, "warning")]
    [InlineData(30, "warning")]     // with warningDays = 30
    [InlineData(31, "ok")]
    public void Classify_MatchesPolicy(int days, string expected)
    {
        Assert.Equal(expected, ExpiryPolicy.Classify(days, warningDays: 30));
    }

    [Fact]
    public void Expiry_DayItself_IsNotYetExpired()
    {
        // Policy: valid through the expiry calendar day.
        Assert.False(ExpiryPolicy.IsExpired(0));
        Assert.True(ExpiryPolicy.IsExpired(-1));
    }
}

public class PermissionCatalogueTests
{
    [Fact]
    public void AllPermissions_AreUnique()
    {
        Assert.Equal(Permissions.All.Length, Permissions.All.Distinct().Count());
    }

    [Fact]
    public void EveryRoleTemplate_UsesKnownPermissionsOnly()
    {
        foreach (var (_, perms) in Permissions.RoleTemplates)
            Assert.All(perms, p => Assert.Contains(p, Permissions.All));
    }

    [Fact]
    public void BusinessOwnerRole_HasAllPermissions()
    {
        Assert.Equal(Permissions.All.Length, Permissions.RoleTemplates["Business Owner"].Length);
    }
}
