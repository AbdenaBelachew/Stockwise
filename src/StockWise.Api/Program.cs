using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using StockWise.Api.Infrastructure;
using StockWise.Application.Abstractions;
using StockWise.Application.Common;
using StockWise.Application.Features.Auth;
using StockWise.Application.Features.Catalog;
using StockWise.Application.Features.Insights;
using StockWise.Application.Features.Inventory;
using StockWise.Application.Features.Organizations;
using StockWise.Application.Features.Procurement;
using StockWise.Application.Features.Products;
using StockWise.Infrastructure.Persistence;
using StockWise.Infrastructure.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------- Logging ----------
builder.Host.UseSerilog((context, config) =>
{
    config.ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

// ---------- Application services ----------
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<OrganizationService>();
builder.Services.AddScoped<CatalogService>();
builder.Services.AddScoped<SupplierService>();
builder.Services.AddScoped<ProductService>();
builder.Services.AddScoped<InventoryPostingService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<TransferService>();
builder.Services.AddScoped<StockCountService>();
builder.Services.AddScoped<PurchaseOrderService>();
builder.Services.AddScoped<GoodsReceiptService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<AuditQueryService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<IdempotencyService>();
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IPermissionChecker, PermissionChecker>();
builder.Services.AddScoped<INumberGenerator, NumberGenerator>();
builder.Services.AddScoped<IEmailSender, DevEmailSender>();
builder.Services.AddMemoryCache();

// ---------- Tenancy & current user ----------
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();

// ---------- Database ----------
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");
builder.Services.AddDbContext<StockWiseDbContext>(options =>
{
    options.UseSnakeCaseNamingConvention();
    // NOTE: no retry-on-failure execution strategy — the app uses explicit transactions
    // with row locks for stock postings, which are incompatible with automatic retries.
    options.UseNpgsql(connectionString);
    if (builder.Environment.IsDevelopment()) options.EnableSensitiveDataLogging();
});
builder.Services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<StockWiseDbContext>());

// ---------- Authentication ----------
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    if (builder.Environment.IsProduction())
        throw new InvalidOperationException(
            "Jwt:Key is missing or shorter than 32 chars. Set Jwt__Key (env) or Jwt:Key (config).");
    jwtKey = "dev-only-stockwise-jwt-signing-key-0123456789abcdef-CHANGE-ME";
    Log.Warning("Using the development fallback JWT key. Never run production without Jwt__Key.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false; // keep original claim types: sub, email, name, tid
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "stockwise",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "stockwise-web",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // Allow the access token via cookie for same-origin deployments (optional path).
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
builder.Services.AddAuthorization();

// ---------- Web defaults ----------
builder.Services.AddControllers();
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "StockWise API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste the access token returned by /api/v1/auth/login."
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("frontend", policy =>
    {
        var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
        policy.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
    });
});

builder.Services.AddHealthChecks()
    .AddDbContextCheck<StockWiseDbContext>("database", tags: ["ready"]);

// ---------- Rate limiting (auth + write endpoints) ----------
var authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);
var writePermitLimit = builder.Configuration.GetValue("RateLimiting:WritePermitLimit", 120);
var globalPermitLimit = builder.Configuration.GetValue("RateLimiting:GlobalPermitLimit", 600);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = authPermitLimit, Window = TimeSpan.FromMinutes(1) }));
    options.AddPolicy("write", context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = writePermitLimit, Window = TimeSpan.FromMinutes(1) }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = globalPermitLimit, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

// ---------- Middleware pipeline ----------
app.UseForwardedHeaders();
app.UseSerilogRequestLogging();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseCors("frontend");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    // Establish the tenant context for query filters from the authenticated principal's active organization.
    if (context.User.Identity?.IsAuthenticated == true &&
        Guid.TryParse(context.User.FindFirstValue("tid"), out var tenantId))
    {
        context.RequestServices.GetRequiredService<ITenantContext>().SetTenant(tenantId);
    }
    await next(context);
});
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Explicit development seeding: `dotnet run -- seed-demo` (see README).
if (args.Contains("seed-demo"))
{
    using var seedScope = app.Services.CreateScope();
    await StockWise.Infrastructure.Persistence.DatabaseSeeder.SeedDemoAsync(seedScope.ServiceProvider);
    return;
}

// Apply migrations automatically in development for a smooth first run.
if (app.Environment.IsDevelopment() && builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<StockWiseDbContext>();
    await db.Database.MigrateAsync();
    Log.Information("Database migrations applied (development mode).");
}

app.Run();

public partial class Program { }
