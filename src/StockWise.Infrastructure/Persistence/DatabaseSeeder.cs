using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StockWise.Application.Abstractions;
using StockWise.Application.Features.Auth;
using StockWise.Application.Common;
using StockWise.Application.Features.Inventory;
using StockWise.Domain.Entities;
using StockWise.Domain.Enums;

namespace StockWise.Infrastructure.Persistence;

/// <summary>
/// Explicit development-only seeding. Never runs automatically in production.
/// Usage: dotnet run --project src/StockWise.Api -- seed-demo
/// Creates reference data (plans) always; with "seed-demo" also a full demo organization.
/// </summary>
public static class DatabaseSeeder
{
    public static async Task SeedReferenceDataAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<StockWiseDbContext>();
        if (!await db.SubscriptionPlans.AnyAsync(ct))
        {
            db.SubscriptionPlans.AddRange(
                new SubscriptionPlan { Code = "FREE", Name = "Free", MaxUsers = 3, MaxWarehouses = 1, MaxActiveProducts = 100, PriceMonthly = 0 },
                new SubscriptionPlan { Code = "STANDARD", Name = "Standard", MaxUsers = 10, MaxWarehouses = 5, MaxActiveProducts = 5000, PriceMonthly = 49 },
                new SubscriptionPlan { Code = "PRO", Name = "Pro", MaxUsers = 50, MaxWarehouses = 25, MaxActiveProducts = 50000, PriceMonthly = 149 });
            await db.SaveChangesAsync(ct);
        }
    }

    public static async Task SeedDemoAsync(IServiceProvider services, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<StockWiseDbContext>();
        await SeedReferenceDataAsync(services, ct);

        const string adminEmail = "admin@demo.stockwise.local";
        if (await db.Users.AnyAsync(u => u.Email == adminEmail, ct))
        {
            Console.WriteLine("Demo data already present; skipping seed.");
            return;
        }

        var hasher = services.GetRequiredService<IPasswordHasher>();
        var authService = services.GetRequiredService<AuthService>();

        // Register the demo owner through the real registration flow so all side effects apply.
        var org = await authService.RegisterAsync(new RegisterRequest(
            "Demo Owner", adminEmail, "DemoPassw0rd!",
            "Northwind Retail Demo", "Retail", "Germany",
            "Europe/Berlin", "EUR", "dd.MM.yyyy"), ct);

        var tenantId = org.OrganizationId;
        // Activate the tenant scope so global query filters see the seeded organization.
        services.GetRequiredService<ITenantContext>().SetTenant(tenantId);

        var demoAdmin = await db.Users.FirstAsync(u => u.Email == adminEmail, ct);
        demoAdmin.EmailVerified = true;
        await db.SaveChangesAsync(ct);

        // Give the (console) seed run an authenticated context so permission checks in
        // application services behave exactly as they would over HTTP.
        var httpAccessor = services.GetRequiredService<IHttpContextAccessor>();
        httpAccessor.HttpContext = new DefaultHttpContext();
        httpAccessor.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, demoAdmin.Id.ToString()),
                new System.Security.Claims.Claim("tid", tenantId.ToString()),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Email, adminEmail)
            }, "seed"));

        // Extra members
        var users = new[]
        {
            ("manager@demo.stockwise.local", "Inventory Manager", "Inventory Manager"),
            ("buyer@demo.stockwise.local", "Procurement Officer", "Procurement Officer"),
            ("store@demo.stockwise.local", "Storekeeper", "Storekeeper"),
            ("approver@demo.stockwise.local", "Approver User", "Approver")
        };
        foreach (var (email, name, roleName) in users)
        {
            var user = new User
            {
                Email = email, FullName = name, PasswordHash = hasher.Hash("DemoPassw0rd!"), EmailVerified = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync(ct);
            var role = await db.Roles.FirstAsync(r => r.TenantId == tenantId && r.Name == roleName, ct);
            db.TenantMemberships.Add(new TenantMembership { UserId = user.Id, OrganizationId = tenantId, RoleId = role.Id });
            await db.SaveChangesAsync(ct);
        }

        var warehouses = await db.Warehouses.Where(w => w.TenantId == tenantId).ToListAsync(ct);
        var main = warehouses.First(w => w.Code == "MAIN");
        var west = new Warehouse { TenantId = tenantId, Name = "West Branch", Code = "WEST", City = "Berlin", Country = "Germany", IsActive = true };
        db.Warehouses.Add(west);
        await db.SaveChangesAsync(ct);

        var unitPcs = await db.Units.FirstAsync(u => u.TenantId == tenantId && u.Symbol == "pcs", ct);
        var unitKg = await db.Units.FirstAsync(u => u.TenantId == tenantId && u.Symbol == "kg", ct);
        var unitBox = await db.Units.FirstAsync(u => u.TenantId == tenantId && u.Symbol == "box", ct);

        var categories = new[]
        {
            new Category { TenantId = tenantId, Name = "Beverages" },
            new Category { TenantId = tenantId, Name = "Dairy" },
            new Category { TenantId = tenantId, Name = "Household" },
            new Category { TenantId = tenantId, Name = "Snacks" }
        };
        db.Categories.AddRange(categories);
        await db.SaveChangesAsync(ct);

        var supplier = new Supplier
        {
            TenantId = tenantId, Code = "SUP-001", Name = "Metro Wholesale GmbH",
            ContactPerson = "Anna Klein", Email = "sales@metrowholesale.example", Phone = "+49 30 123456",
            Country = "Germany", PaymentTermsDays = 30, CurrencyCode = "EUR"
        };
        var supplier2 = new Supplier
        {
            TenantId = tenantId, Code = "SUP-002", Name = "FreshFarm Distribution",
            ContactPerson = "Jonas Weber", Email = "orders@freshfarm.example", Phone = "+49 89 654321",
            Country = "Germany", PaymentTermsDays = 14, CurrencyCode = "EUR"
        };
        db.Suppliers.AddRange(supplier, supplier2);
        await db.SaveChangesAsync(ct);

        var products = new[]
        {
            new Product { TenantId = tenantId, Sku = "BEV-001", Name = "Sparkling Water 1L", CategoryId = categories[0].Id, UnitId = unitPcs.Id, CostPrice = 0.45m, SalePrice = 0.95m, ReorderPoint = 100, TargetStockLevel = 400, DefaultSupplierId = supplier.Id, TrackBatches = false },
            new Product { TenantId = tenantId, Sku = "BEV-002", Name = "Cola 330ml (24 pack)", CategoryId = categories[0].Id, UnitId = unitBox.Id, CostPrice = 9.60m, SalePrice = 14.50m, ReorderPoint = 20, TargetStockLevel = 60, DefaultSupplierId = supplier.Id, TrackBatches = false },
            new Product { TenantId = tenantId, Sku = "DRY-001", Name = "Whole Milk 1L", CategoryId = categories[1].Id, UnitId = unitPcs.Id, CostPrice = 0.85m, SalePrice = 1.35m, ReorderPoint = 60, TargetStockLevel = 200, DefaultSupplierId = supplier2.Id, TrackBatches = true, TrackExpiry = true, MinShelfLifeDays = 5 },
            new Product { TenantId = tenantId, Sku = "DRY-002", Name = "Greek Yogurt 500g", CategoryId = categories[1].Id, UnitId = unitPcs.Id, CostPrice = 1.20m, SalePrice = 2.10m, ReorderPoint = 40, TargetStockLevel = 150, DefaultSupplierId = supplier2.Id, TrackBatches = true, TrackExpiry = true, MinShelfLifeDays = 7 },
            new Product { TenantId = tenantId, Sku = "HOU-001", Name = "Dish Soap 500ml", CategoryId = categories[2].Id, UnitId = unitPcs.Id, CostPrice = 1.05m, SalePrice = 1.99m, ReorderPoint = 50, TargetStockLevel = 150, DefaultSupplierId = supplier.Id, TrackBatches = false },
            new Product { TenantId = tenantId, Sku = "SNK-001", Name = "Salted Chips 175g", CategoryId = categories[3].Id, UnitId = unitPcs.Id, CostPrice = 0.70m, SalePrice = 1.49m, ReorderPoint = 80, TargetStockLevel = 250, DefaultSupplierId = supplier.Id, TrackBatches = false },
            new Product { TenantId = tenantId, Sku = "SNK-002", Name = "Dark Chocolate 100g", CategoryId = categories[3].Id, UnitId = unitPcs.Id, CostPrice = 0.95m, SalePrice = 1.85m, ReorderPoint = 60, TargetStockLevel = 200, DefaultSupplierId = supplier.Id, TrackBatches = false }
        };
        db.Products.AddRange(products);
        await db.SaveChangesAsync(ct);

        // Opening balances through the real posting service (writes proper ledger rows).
        var currentUser = services.GetRequiredService<ICurrentUser>();

        var today = DateTime.UtcNow;
        var opening = new[]
        {
            (products[0], 320m, 0.45m, (string?)null, (DateTime?)null),
            (products[1], 45m, 9.60m, null, null),
            (products[2], 150m, 0.85m, "MILK-2026-081", (DateTime?)today.AddDays(20)),
            (products[2], 60m, 0.85m, "MILK-2026-082", (DateTime?)today.AddDays(12)),
            (products[3], 90m, 1.20m, "YOG-2026-045", (DateTime?)today.AddDays(9)),
            (products[4], 120m, 1.05m, null, null),
            (products[5], 200m, 0.70m, null, null),
            (products[6], 110m, 0.95m, null, null)
        };
        var inventoryService = services.GetRequiredService<InventoryService>();
        await inventoryService.CreateOpeningBalanceAsync(new CreateOpeningBalanceRequest(
            main.Id,
            opening.Select(o => new AdjustmentLine(o.Item1.Id, o.Item2, null, o.Item4, o.Item5, "Opening balance", o.Item3)).ToList(),
            "demo-opening-balances"), ct);

        // West branch gets a little stock of two products.
        await inventoryService.CreateOpeningBalanceAsync(new CreateOpeningBalanceRequest(
            west.Id,
            new List<AdjustmentLine>
            {
                new(products[0].Id, 80, null, null, null, "Opening balance", 0.45m),
                new(products[5].Id, 40, null, null, null, "Opening balance", 0.70m)
            },
            "demo-opening-balances-west"), ct);

        // A purchase order awaiting approval (created by demo admin).
        var po = new PurchaseOrder
        {
            TenantId = tenantId, Number = "PO-000001", SupplierId = supplier.Id, WarehouseId = main.Id,
            CurrencyCode = "EUR", Status = PurchaseOrderStatus.PendingApproval,
            ExpectedDeliveryDate = today.AddDays(7), Notes = "Weekly replenishment", CreatedBy = demoAdmin.Id,
            SubmittedAt = today
        };
        po.Lines.Add(new PurchaseOrderLine { ProductId = products[0].Id, OrderedQuantity = 500, UnitCost = 0.44m });
        po.Lines.Add(new PurchaseOrderLine { ProductId = products[1].Id, OrderedQuantity = 30, UnitCost = 9.40m, TaxPercent = 19 });
        po.SubTotal = po.Lines.Sum(l => l.OrderedQuantity * l.UnitCost);
        po.TaxTotal = po.Lines.Sum(l => l.OrderedQuantity * l.UnitCost * l.TaxPercent / 100m);
        po.Total = po.SubTotal + po.TaxTotal;
        po.Approvals.Add(new PurchaseApproval { PurchaseOrderId = po.Id, Action = ApprovalAction.Submitted, ActorUserId = demoAdmin.Id });
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync(ct);

        Console.WriteLine();
        Console.WriteLine("======================================================");
        Console.WriteLine(" StockWise demo data seeded.");
        Console.WriteLine($"   Owner:    {adminEmail} / DemoPassw0rd!");
        Console.WriteLine("   Manager:  manager@demo.stockwise.local / DemoPassw0rd!");
        Console.WriteLine("   Buyer:    buyer@demo.stockwise.local / DemoPassw0rd!");
        Console.WriteLine("   Store:    store@demo.stockwise.local / DemoPassw0rd!");
        Console.WriteLine("   Approver: approver@demo.stockwise.local / DemoPassw0rd!");
        Console.WriteLine(" (DEVELOPMENT ONLY credentials - never use in production)");
        Console.WriteLine("======================================================");
    }
}
