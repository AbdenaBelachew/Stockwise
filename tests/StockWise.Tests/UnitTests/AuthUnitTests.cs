using Microsoft.Extensions.Configuration;
using StockWise.Infrastructure.Services;
using Xunit;

namespace StockWise.UnitTests;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_And_Verify_RoundTrip()
    {
        var hash = _hasher.Hash("DemoPassw0rd!");
        Assert.NotEqual("DemoPassw0rd!", hash);
        Assert.True(_hasher.Verify("DemoPassw0rd!", hash));
        Assert.False(_hasher.Verify("wrong", hash));
    }

    [Fact]
    public void Hash_IsSaltedPerCall()
    {
        Assert.NotEqual(_hasher.Hash("same-password"), _hasher.Hash("same-password"));
    }

    [Fact]
    public void Verify_MalformedHash_ReturnsFalse()
    {
        Assert.False(_hasher.Verify("x", "not-a-bcrypt-hash"));
    }
}

public class TokenHashingTests
{
    private readonly TokenService _tokens = new(
        new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Key"] = new string('k', 40) })
            .Build(),
        MockLogger.Instance);

    [Fact]
    public void TokenHash_IsDeterministic()
    {
        var h1 = _tokens.HashToken("opaque-token-value");
        var h2 = _tokens.HashToken("opaque-token-value");
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length); // SHA-256 hex
    }

    [Fact]
    public void AccessToken_ContainsTenantAndUserClaims()
    {
        var user = new StockWise.Domain.Entities.User
        {
            Id = Guid.NewGuid(), Email = "user@test.local", FullName = "Test User"
        };
        var tenantId = Guid.NewGuid();
        var (token, expires) = _tokens.CreateAccessToken(user, tenantId, "Test Org");

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(tenantId.ToString(), jwt.Claims.First(c => c.Type == "tid").Value);
        Assert.True(expires > DateTime.UtcNow);
    }

    [Fact]
    public void AccessToken_Lifetime_IsShort()
    {
        var user = new StockWise.Domain.Entities.User { Id = Guid.NewGuid(), Email = "u@t.local", FullName = "U" };
        var (_, expires) = _tokens.CreateAccessToken(user, Guid.NewGuid(), "Org");
        Assert.True((expires - DateTime.UtcNow) < TimeSpan.FromMinutes(16));
    }
}

internal sealed class MockLogger : Microsoft.Extensions.Logging.ILogger<TokenService>
{
    public static MockLogger Instance { get; } = new();
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
        TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
}
