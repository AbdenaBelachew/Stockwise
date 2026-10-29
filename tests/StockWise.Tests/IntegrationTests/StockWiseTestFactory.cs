using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace StockWise.IntegrationTests;

/// <summary>
/// Shared API test server backed by a disposable PostgreSQL container.
/// Migrations are applied once per collection; each test class gets the same
/// running application so cross-tenant scenarios can share real state.
/// </summary>
public class StockWiseTestFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    static StockWiseTestFactory()
    {
        // Relaxed rate limits for the suite; production defaults remain in appsettings.
        Environment.SetEnvironmentVariable("RateLimiting__AuthPermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__WritePermitLimit", "100000");
        Environment.SetEnvironmentVariable("RateLimiting__GlobalPermitLimit", "1000000");
    }

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("stockwise_test")
        .WithUsername("stockwise")
        .WithPassword("test_pw")
        .Build();

    public static StockWiseTestFactory? Instance { get; private set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _db.GetConnectionString(),
                ["Database:ApplyMigrationsOnStartup"] = "false",
                ["RateLimiting:AuthPermitLimit"] = "100000",
                ["RateLimiting:WritePermitLimit"] = "100000",
                ["RateLimiting:GlobalPermitLimit"] = "1000000"
            });
        });
    }

    public async Task InitializeAsync()
    {
        await _db.StartAsync();
        Instance = this;
        // Apply the schema through the app's own migration pipeline.
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StockWise.Infrastructure.Persistence.StockWiseDbContext>();
        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiTestCollection : ICollectionFixture<StockWiseTestFactory>;
