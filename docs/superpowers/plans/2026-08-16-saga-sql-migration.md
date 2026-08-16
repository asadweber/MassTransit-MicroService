# Saga Persistence Migration (Mongo → SQL Server) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `OrderStateMachine`'s saga state persistence from MongoDB to SQL Server (existing `AppDbContext`/OrderDB) via MassTransit's `EntityFrameworkSagaRepository`, flattening `OrderSagaState.Order` into scalar columns + an owned `SagaOrderDetail` collection, and updating `SagaDashboard` to query SQL instead of Mongo directly.

**Architecture:** `OrderSagaState` moves from a Mongo document (nested `OrderDto`) to an EF entity with flattened order fields and an owned `SagaOrderDetail` table, added to the existing `AppDbContext`. `OrderSaga` and `SagaDashboard` both switch their saga repository registration from `.MongoDbRepository(...)` to `.EntityFrameworkRepository(...)` against that same context/connection. Serilog logging keeps using MongoDB (`OrderSagaDb`) — untouched.

**Tech Stack:** .NET 10, EF Core (SQL Server), MassTransit 9.2 (`MassTransit.EntityFrameworkCore`, already referenced), existing `Infrastructure` project conventions (`IEntityTypeConfiguration<T>`, `dotnet ef migrations`).

No test project exists in this solution (per `CLAUDE.md`) — verification steps below are manual build/run checks instead of automated tests.

---

### Task 1: Flatten `OrderSagaState` and add `SagaOrderDetail`

**Files:**
- Modify: `src/OrderSaga/Saga/OrderSagaState.cs`
- Create: `src/OrderSaga/Saga/SagaOrderDetail.cs`

- [ ] **Step 1: Rewrite `OrderSagaState.cs`**

```csharp
using MassTransit;

namespace OrderSaga.Saga;

public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public List<SagaOrderDetail> OrderDetails { get; set; } = [];

    public DateTime? FirstUnavailableAt { get; set; }
    public DateTime? NextInventoryRetryAt { get; set; }

    public int InventoryRetryCount { get; set; } = 0;

    public Guid? InventoryRetryTokenId { get; set; }
}
```

- [ ] **Step 2: Create `SagaOrderDetail.cs`**

```csharp
namespace OrderSaga.Saga;

public class SagaOrderDetail
{
    public int Id { get; set; }
    public Guid OrderSagaStateCorrelationId { get; set; }
    public int ProductId { get; set; }
    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
```

- [ ] **Step 3: Build to confirm the removed Mongo attributes don't break anything else yet**

Run: `dotnet build src/OrderSaga/OrderSaga.csproj`
Expected: FAILS — `OrderStateMachine.cs` and `OrderSagaDefinition.cs` still reference `ctx.Saga.Order`. This is expected; those are fixed in Task 2.

- [ ] **Step 4: Commit**

```bash
git add src/OrderSaga/Saga/OrderSagaState.cs src/OrderSaga/Saga/SagaOrderDetail.cs
git commit -m "Flatten OrderSagaState.Order into scalar fields + SagaOrderDetail"
```

---

### Task 2: Update `OrderStateMachine` and `OrderSagaDefinition` for flattened state

**Files:**
- Modify: `src/OrderSaga/Saga/OrderStateMachine.cs`
- Modify: `src/OrderSaga/Saga/OrderSagaDefinition.cs`

- [ ] **Step 1: Fix the `OrderCreated` correlation (line 73) and the state-set on arrival (lines 93-97)**

Replace:
```csharp
        Event(() => OrderCreated, x =>
            x.CorrelateBy((instance, context) => instance.Order == context.Message.Order)
             .SelectId(_ => NewId.NextGuid()));
```
with:
```csharp
        Event(() => OrderCreated, x =>
            x.CorrelateBy((instance, context) => instance.OrderId == context.Message.Order.Id)
             .SelectId(_ => NewId.NextGuid()));
```

Replace the `Then` in `Initially(When(OrderCreated)...)`:
```csharp
                .Then(ctx =>
                {
                    ctx.Saga.Order = ctx.Message.Order;
                    Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Saga.CorrelationId);
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.Order.Id);
                })
```
with:
```csharp
                .Then(ctx =>
                {
                    var order = ctx.Message.Order;
                    ctx.Saga.OrderId = order.Id;
                    ctx.Saga.CustomerName = order.CustomerName;
                    ctx.Saga.OrderDate = order.OrderDate;
                    ctx.Saga.TotalAmount = order.TotalAmount;
                    ctx.Saga.Status = order.Status;
                    ctx.Saga.OrderDetails = order.OrderDetails.Select(d => new SagaOrderDetail
                    {
                        OrderSagaStateCorrelationId = ctx.Saga.CorrelationId,
                        ProductId = d.ProductId,
                        OrderQty = d.OrderQty,
                        UnitPrice = d.UnitPrice,
                        Total = d.Total,
                    }).ToList();

                    Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Saga.CorrelationId);
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.OrderId);
                })
```

- [ ] **Step 2: Add a helper to rebuild an `OrderDto` from the flattened saga state**

Every `PublishAsync(ctx => ctx.Init<...>(new ... { Order = ctx.Saga.Order }))` needs a rebuilt `OrderDto`. Add a private method near the bottom of the class (after `IsRetryWindowExpired`):

```csharp
    private static OrderDto ToOrderDto(OrderSagaState saga) => new()
    {
        Id = saga.OrderId,
        CustomerName = saga.CustomerName,
        OrderDate = saga.OrderDate,
        TotalAmount = saga.TotalAmount,
        Status = saga.Status,
        OrderDetails = saga.OrderDetails.Select(d => new OrderDetailDto
        {
            Id = d.Id,
            OrderId = saga.OrderId,
            ProductId = d.ProductId,
            OrderQty = d.OrderQty,
            UnitPrice = d.UnitPrice,
            Total = d.Total,
        }).ToList(),
    };
```

Add `using Application.Dtos;` to the top of `OrderStateMachine.cs`.

- [ ] **Step 3: Replace every `Order = ctx.Saga.Order` with `Order = ToOrderDto(ctx.Saga)`**

There are 5 occurrences (lines 102, 122, 165, 187, 202 in the original file) inside `CheckInventory`/`ProcessPayment`/`OrderConfirmed` message inits. Replace each `Order = ctx.Saga.Order,` with `Order = ToOrderDto(ctx.Saga),`.

- [ ] **Step 4: Replace every `ctx.Saga.Order.Id` with `ctx.Saga.OrderId`**

Occurrences in log calls: lines 117, 143, 170, 183, 207, 214 (in the original file — logging statements only, e.g. `ctx.Saga.Order.Id, ctx.Saga.CorrelationId`). Replace each `ctx.Saga.Order.Id` with `ctx.Saga.OrderId`.

- [ ] **Step 5: Fix `OrderSagaDefinition.cs` — swap the Mongo concurrency exception for EF's**

Replace:
```csharp
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Handle<MongoDbConcurrencyException>();
                r.Interval(10, TimeSpan.FromMilliseconds(100));
            });
```
with:
```csharp
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Handle<DbUpdateConcurrencyException>();
                r.Interval(10, TimeSpan.FromMilliseconds(100));
            });
```
Add `using Microsoft.EntityFrameworkCore;` to the top of `OrderSagaDefinition.cs`. Update the comment above it (currently "Mongo optimistic-concurrency conflicts...") to say "EF Core optimistic-concurrency conflicts (two messages racing to update the same saga row)".

Also update the comment on `ConcurrentMessageLimit = 16` (currently references "Mongo optimistic-concurrency writes") to say "SQL Server optimistic-concurrency writes".

Fix `context.Message.Order.Id` at line 91 — no change needed, `OrderCreated` event's `.Order` is still the full `OrderDto` (this reads the incoming message, not saga state).

- [ ] **Step 6: Build**

Run: `dotnet build src/OrderSaga/OrderSaga.csproj`
Expected: SUCCESS (0 errors).

- [ ] **Step 7: Commit**

```bash
git add src/OrderSaga/Saga/OrderStateMachine.cs src/OrderSaga/Saga/OrderSagaDefinition.cs
git commit -m "Update OrderStateMachine/OrderSagaDefinition for flattened saga state"
```

---

### Task 3: Add EF configuration for `OrderSagaState` to `AppDbContext`

**Files:**
- Create: `src/Infrastructure/Persistence/Configurations/OrderSagaStateConfiguration.cs`
- Modify: `src/Infrastructure/Persistence/AppDbContext.cs`

- [ ] **Step 1: Create the configuration**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderSaga.Saga;

namespace Infrastructure.Persistence.Configurations;

public class OrderSagaStateConfiguration : IEntityTypeConfiguration<OrderSagaState>
{
    public void Configure(EntityTypeBuilder<OrderSagaState> builder)
    {
        builder.ToTable("OrderSagaStates");

        builder.HasKey(s => s.CorrelationId);

        builder.Property(s => s.Version).IsConcurrencyToken();

        builder.Property(s => s.CustomerName).HasMaxLength(200);
        builder.Property(s => s.TotalAmount).HasPrecision(18, 2);
        builder.Property(s => s.Status).HasMaxLength(50);

        builder.OwnsMany(s => s.OrderDetails, detail =>
        {
            detail.ToTable("SagaOrderDetails");
            detail.WithOwner().HasForeignKey(d => d.OrderSagaStateCorrelationId);
            detail.HasKey(d => d.Id);
            detail.Property(d => d.UnitPrice).HasPrecision(18, 2);
            detail.Property(d => d.Total).HasPrecision(18, 2);
        });
    }
}
```

This introduces a circular-looking dependency (`Infrastructure` → `OrderSaga.Saga`), but `Infrastructure` currently has no project reference to `OrderSaga` — it needs one, added in Step 2 below. `OrderSaga` already references `Infrastructure` (for `AddInfrastructure()`), so this file lives in `Infrastructure` but the *type* it configures lives in `OrderSaga` — this direction is genuinely circular and gets resolved in Step 5.

- [ ] **Step 2: Add project reference `Infrastructure` → `OrderSaga`**

Modify `src/Infrastructure/Infrastructure.csproj` — add inside the existing `<ItemGroup>` with `<ProjectReference>` entries:
```xml
    <ProjectReference Include="..\OrderSaga\OrderSaga.csproj" />
```

- [ ] **Step 3: Register the `DbSet` in `AppDbContext.cs`**

Modify `src/Infrastructure/Persistence/AppDbContext.cs`:
```csharp
using Domain.Entities;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderSaga.Saga;

namespace Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

- [ ] **Step 4: Build — this WILL fail with a circular project reference**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: FAIL — `OrderSaga.csproj` references `Infrastructure.csproj` (for `AddInfrastructure()`), and step 2 just made `Infrastructure.csproj` reference `OrderSaga.csproj` back. This is a real circular dependency, not a false alarm — resolved in Step 5.

- [ ] **Step 5: Break the cycle — move `OrderSagaState`/`SagaOrderDetail` into `Infrastructure`**

The circular reference means `OrderSagaState` can't stay owned by `OrderSaga` if `Infrastructure` needs to configure it as an EF entity. Move both types into `Infrastructure`:

Delete `src/OrderSaga/Saga/OrderSagaState.cs` and `src/OrderSaga/Saga/SagaOrderDetail.cs`.

Create `src/Infrastructure/Persistence/OrderSagaState.cs`:
```csharp
using MassTransit;

namespace Infrastructure.Persistence;

public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    public int OrderId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = "Pending";

    public List<SagaOrderDetail> OrderDetails { get; set; } = [];

    public DateTime? FirstUnavailableAt { get; set; }
    public DateTime? NextInventoryRetryAt { get; set; }

    public int InventoryRetryCount { get; set; } = 0;

    public Guid? InventoryRetryTokenId { get; set; }
}
```

Create `src/Infrastructure/Persistence/SagaOrderDetail.cs`:
```csharp
namespace Infrastructure.Persistence;

public class SagaOrderDetail
{
    public int Id { get; set; }
    public Guid OrderSagaStateCorrelationId { get; set; }
    public int ProductId { get; set; }
    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
```

Revert `src/Infrastructure/Infrastructure.csproj` — remove the `<ProjectReference Include="..\OrderSaga\OrderSaga.csproj" />` added in Step 2.

Update `OrderSagaStateConfiguration.cs`'s using from `OrderSaga.Saga` to `Infrastructure.Persistence`:
```csharp
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;
```

Update `AppDbContext.cs`: remove the `using OrderSaga.Saga;` line entirely — `OrderSagaState` is now in `Infrastructure.Persistence`, the same namespace as `AppDbContext` itself, so no using is needed for it.

Update `src/OrderSaga/Saga/OrderStateMachine.cs` and `src/OrderSaga/Saga/OrderSagaDefinition.cs`: add `using Infrastructure.Persistence;` to both files' usings (they reference `OrderSagaState`/`SagaOrderDetail`, now in `Infrastructure.Persistence` instead of the local `OrderSaga.Saga` namespace).

- [ ] **Step 6: Build `Infrastructure`, then the whole solution**

Run: `dotnet build src/Infrastructure/Infrastructure.csproj`
Expected: SUCCESS.

Run: `dotnet build MicroService.sln`
Expected: Still FAILS on `SagaDashboard` (Task 6 handles it) and possibly `OrderSaga` if any using was missed — fix any remaining `OrderSagaState`/`SagaOrderDetail` reference errors by adding `using Infrastructure.Persistence;` where the compiler points.

- [ ] **Step 7: Commit**

```bash
git add src/Infrastructure/Persistence/Configurations/OrderSagaStateConfiguration.cs src/Infrastructure/Persistence/AppDbContext.cs src/Infrastructure/Persistence/OrderSagaState.cs src/Infrastructure/Persistence/SagaOrderDetail.cs src/OrderSaga/Saga/OrderSagaState.cs src/OrderSaga/Saga/SagaOrderDetail.cs src/OrderSaga/Saga/OrderStateMachine.cs src/OrderSaga/Saga/OrderSagaDefinition.cs
git commit -m "Move OrderSagaState/SagaOrderDetail into Infrastructure, add EF configuration"
```

---

### Task 4: Generate and review the EF migration

**Files:**
- Create: `src/Infrastructure/Migrations/<timestamp>_AddOrderSagaState.cs` (and `.Designer.cs`)
- Modify: `src/Infrastructure/Migrations/AppDbContextModelSnapshot.cs` (auto-generated)

- [ ] **Step 1: Generate the migration**

Run:
```bash
dotnet ef migrations add AddOrderSagaState --project src/Infrastructure --startup-project src/WebApp
```
Expected: Creates `OrderSagaStates` and `SagaOrderDetails` tables in the generated migration. No changes to `Orders`/`Products`/`OrderDetails`.

- [ ] **Step 2: Read the generated migration file and confirm**

Check the `Up()` method creates:
- `OrderSagaStates` table with `CorrelationId` (PK, uniqueidentifier), `Version` (int, concurrency token — no rowversion needed since it's an app-managed int), `OrderId`, `CustomerName` (nvarchar(200)), `OrderDate`, `TotalAmount` (decimal(18,2)), `Status` (nvarchar(50)), `CurrentState`, `FirstUnavailableAt`, `NextInventoryRetryAt`, `InventoryRetryCount`, `InventoryRetryTokenId`.
- `SagaOrderDetails` table with `Id` (PK, int identity), `OrderSagaStateCorrelationId` (FK to `OrderSagaStates.CorrelationId`), `ProductId`, `OrderQty`, `UnitPrice` (decimal(18,2)), `Total` (decimal(18,2)).

If anything is missing or wrong (e.g. `Version` came out as `rowversion`/`timestamp` instead of a plain `int` — EF sometimes infers concurrency tokens as `rowversion` for byte[] but `Version` is `int` here so it should stay `int`), fix `OrderSagaStateConfiguration.cs` and regenerate:
```bash
dotnet ef migrations remove --project src/Infrastructure --startup-project src/WebApp
dotnet ef migrations add AddOrderSagaState --project src/Infrastructure --startup-project src/WebApp
```

- [ ] **Step 3: Apply the migration to the local dev database**

Run:
```bash
dotnet ef database update --project src/Infrastructure --startup-project src/WebApp
```
Expected: SUCCESS — `OrderSagaStates` and `SagaOrderDetails` tables now exist in OrderDB (verify via SQL Server Management Studio or `sqlcmd` if desired).

- [ ] **Step 4: Commit**

```bash
git add src/Infrastructure/Migrations/
git commit -m "Add AddOrderSagaState migration"
```

---

### Task 5: Wire `EntityFrameworkRepository` in `OrderSaga/Program.cs`

**Files:**
- Modify: `src/OrderSaga/Program.cs`
- Modify: `src/OrderSaga/appsettings.json`

- [ ] **Step 1: Replace the saga repository registration**

Replace:
```csharp
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .MongoDbRepository(r =>
        {
            // Use the same connection string — MassTransit will resolve
            // the shared IMongoClient internally via ClientFactory below
            r.Connection = mongoSection.ConnectionString;
            r.DatabaseName = mongoSection.DatabaseName;
            r.CollectionName = mongoSection.SagaCollection;
        });
```
with:
```csharp
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });
```

Add `using Infrastructure.Persistence;` to the top of `Program.cs` (for `AppDbContext` and `OrderSagaState`).

- [ ] **Step 2: Delete the Mongo saga-collection bootstrap block**

Delete lines 58-76 (the `// ── Ensure saga collection exists ─────` block that lists Mongo collections and creates `SagaCollection` if missing). The `var host = builder.Build();` line stays — just remove everything from the comment through the closing `}` of that `using (var scope = ...)` block, keeping `host.Run()` at the end.

Resulting tail of `Program.cs` should read:
```csharp
var host = builder.Build();

//Ensure Serilog TTL index exists (or recreate if retention period changed)
SerilogRetentionSetup.EnsureSerilogTtlIndex(builder.Configuration, retentionDays: 1);

host.Run();
```

- [ ] **Step 3: Remove `SagaCollection` from `appsettings.json`**

Modify `src/OrderSaga/appsettings.json` — the `MongoDb` section stays (still needed for Serilog's TTL setup which reads `MongoDb:DatabaseName` indirectly via the hardcoded Serilog config, and `mongoSection` is still referenced for that), but remove the now-unused `SagaCollection` key:
```json
  "MongoDb": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "OrderSagaDb"
  },
```

- [ ] **Step 4: Build and run**

Run: `dotnet build src/OrderSaga/OrderSaga.csproj`
Expected: SUCCESS.

Run: `dotnet run --project src/OrderSaga`
Expected: Starts without error, connects to RabbitMQ and SQL Server (OrderDB). Stop with Ctrl+C once confirmed no startup exceptions.

- [ ] **Step 5: Commit**

```bash
git add src/OrderSaga/Program.cs src/OrderSaga/appsettings.json
git commit -m "Wire OrderSaga to EntityFrameworkSagaRepository, drop Mongo saga bootstrap"
```

---

### Task 6: Rewrite `SagaDashboard` to query SQL instead of Mongo

**Files:**
- Modify: `src/SagaDashboard/Program.cs`
- Modify: `src/SagaDashboard/Controllers/OrderSagasController.cs`
- Modify: `src/SagaDashboard/Views/OrderSagas/Index.cshtml`
- Modify: `src/SagaDashboard/appsettings.json`

- [ ] **Step 1: Update `Program.cs` — drop the Mongo collection singleton, switch repository**

Replace:
```csharp
var mongoSection = builder.Configuration.GetSection("MongoDb").Get<MongoDbSettings>();

// Direct Mongo access for the saga list page (read-only, separate from MassTransit's own repository).
builder.Services.AddSingleton(sp =>
{
    var client = new MongoClient(mongoSection!.ConnectionString);
    var db = client.GetDatabase(mongoSection.DatabaseName);
    return db.GetCollection<OrderSagaState>(mongoSection.SagaCollection);
});
```
with nothing (delete this block entirely — `AppDbContext` from `AddInfrastructure()` already provides SQL access, no separate singleton needed).

Replace:
```csharp
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, DashboardOnlySagaDefinition>()
        .MongoDbRepository(r =>
        {
            r.Connection = mongoSection!.ConnectionString;
            r.DatabaseName = mongoSection.DatabaseName;
            r.CollectionName = mongoSection.SagaCollection;
        });
```
with:
```csharp
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, DashboardOnlySagaDefinition>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });
```

Replace `using MongoDB.Driver;` and `using OrderSaga.Saga;` with `using Infrastructure.Persistence;` at the top of the file (drop the now-unused `MongoDB.Driver` using, `OrderSagaState` now lives in `Infrastructure.Persistence`).

- [ ] **Step 2: Rewrite `OrderSagasController.cs`**

```csharp
using Application.Dtos;
using Application.Interfaces;
using Application.Messaging.Events;
using Infrastructure.Persistence;
using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace SagaDashboard.Controllers;

public class OrderSagasController : Controller
{
    private readonly AppDbContext _db;
    private readonly IOrderService _orderService;
    private readonly IProductService _productService;
    private readonly IPublishEndpoint _bus;

    public OrderSagasController(
        AppDbContext db,
        IOrderService orderService,
        IProductService productService,
        IPublishEndpoint bus)
    {
        _db = db;
        _orderService = orderService;
        _productService = productService;
        _bus = bus;
    }

    public async Task<IActionResult> Index(int? orderId, string? state, CancellationToken ct)
    {
        var query = _db.OrderSagaStates.Include(s => s.OrderDetails).AsQueryable();

        if (orderId.HasValue)
        {
            query = query.Where(s => s.OrderId == orderId.Value);
        }

        if (!string.IsNullOrWhiteSpace(state))
        {
            query = query.Where(s => s.CurrentState == state);
        }

        var results = await query
            .OrderByDescending(s => s.OrderDate)
            .Take(200)
            .ToListAsync(ct);

        ViewBag.OrderId = orderId;
        ViewBag.State = state;

        return View(results);
    }

    // Reachable for a Failed saga (restart from scratch) or a CheckingInventory saga stuck
    // retrying (edit line items in place — the next scheduled retry re-checks saga.Order).
    public async Task<IActionResult> Edit(Guid correlationId, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.Include(s => s.OrderDetails)
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || (saga.CurrentState != "Failed" && saga.CurrentState != "CheckingInventory"))
            return NotFound();

        ViewBag.Products = await _productService.GetAllAsync();
        ViewBag.SagaState = saga.CurrentState;
        return View(ToOrderDto(saga));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Restart(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || saga.CurrentState != "Failed") return NotFound();
        if (saga.OrderId != request.Id) return NotFound();

        var orderId = saga.OrderId;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        // Drop the terminated saga instance and re-publish OrderCreated to start a fresh saga
        // with the corrected order data.
        _db.OrderSagaStates.Remove(saga);
        await _db.SaveChangesAsync(ct);

        var order = await _orderService.GetByIdAsync(orderId);
        await _bus.Publish(new OrderCreated { Order = order! }, ct);

        return RedirectToAction(nameof(Index));
    }

    // Live edit for a saga stuck retrying CheckingInventory: updates the SQL order row and the
    // saga's own Order snapshot in place, without touching saga state or the pending retry
    // schedule — the next InventoryRetry fire re-checks stock against the corrected line items.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateInFlight(Guid correlationId, OrderDto request, CancellationToken ct)
    {
        var saga = await _db.OrderSagaStates.Include(s => s.OrderDetails)
            .FirstOrDefaultAsync(s => s.CorrelationId == correlationId, ct);
        if (saga is null || saga.CurrentState != "CheckingInventory") return NotFound();
        if (saga.OrderId != request.Id) return NotFound();

        var orderId = saga.OrderId;
        var updated = await _orderService.UpdateAsync(orderId, request);
        if (!updated) return NotFound();

        var order = await _orderService.GetByIdAsync(orderId);

        saga.CustomerName = order!.CustomerName;
        saga.OrderDate = order.OrderDate;
        saga.TotalAmount = order.TotalAmount;
        saga.Status = order.Status;
        saga.OrderDetails = order.OrderDetails.Select(d => new SagaOrderDetail
        {
            OrderSagaStateCorrelationId = saga.CorrelationId,
            ProductId = d.ProductId,
            OrderQty = d.OrderQty,
            UnitPrice = d.UnitPrice,
            Total = d.Total,
        }).ToList();

        await _db.SaveChangesAsync(ct);

        return RedirectToAction(nameof(Index));
    }

    private static OrderDto ToOrderDto(OrderSagaState saga) => new()
    {
        Id = saga.OrderId,
        CustomerName = saga.CustomerName,
        OrderDate = saga.OrderDate,
        TotalAmount = saga.TotalAmount,
        Status = saga.Status,
        OrderDetails = saga.OrderDetails.Select(d => new OrderDetailDto
        {
            Id = d.Id,
            OrderId = saga.OrderId,
            ProductId = d.ProductId,
            OrderQty = d.OrderQty,
            UnitPrice = d.UnitPrice,
            Total = d.Total,
        }).ToList(),
    };
}
```

Note: `UpdateInFlight` replaces the entire `OrderDetails` list rather than diffing — EF's owned-collection convention treats collection reassignment as a full replace/re-insert on `SaveChangesAsync`, matching the prior Mongo behavior of overwriting `saga.Order` wholesale via `Builders<...>.Update.Set(s => s.Order, order)`.

- [ ] **Step 3: Update `Index.cshtml` for flattened fields**

Modify `src/SagaDashboard/Views/OrderSagas/Index.cshtml`:

Replace:
```cshtml
@using OrderSaga.Saga
@model IEnumerable<OrderSagaState>
```
with:
```cshtml
@using Infrastructure.Persistence
@model IEnumerable<OrderSagaState>
```

Replace:
```cshtml
                <td>@saga.Order?.Id</td>
                <td>@saga.Order?.CustomerName</td>
                <td>@saga.Order?.OrderDate</td>
                <td>@saga.Order?.TotalAmount</td>
```
with:
```cshtml
                <td>@saga.OrderId</td>
                <td>@saga.CustomerName</td>
                <td>@saga.OrderDate</td>
                <td>@saga.TotalAmount</td>
```

- [ ] **Step 4: Remove unused `MongoDb:SagaCollection` from `appsettings.json`**

Modify `src/SagaDashboard/appsettings.json` the same way as Task 5 Step 3:
```json
  "MongoDb": {
    "ConnectionString": "mongodb://localhost:27017",
    "DatabaseName": "OrderSagaDb"
  },
```
(`MongoDb:ConnectionString`/`DatabaseName` stay in place — only remove the dead `SagaCollection` key.)

- [ ] **Step 5: Build**

Run: `dotnet build src/SagaDashboard/SagaDashboard.csproj`
Expected: SUCCESS.

- [ ] **Step 6: Commit**

```bash
git add src/SagaDashboard/Program.cs src/SagaDashboard/Controllers/OrderSagasController.cs src/SagaDashboard/Views/OrderSagas/Index.cshtml src/SagaDashboard/appsettings.json
git commit -m "Rewrite SagaDashboard to query SQL Server via AppDbContext instead of Mongo"
```

---

### Task 7: Full solution build and manual end-to-end verification

**Files:** none (verification only)

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build MicroService.sln`
Expected: SUCCESS, 0 errors, 0 new warnings beyond pre-existing ones.

- [ ] **Step 2: Start dependent infra**

Confirm SQL Server, MongoDB (still needed for Serilog logs), and RabbitMQ (with `rabbitmq_delayed_message_exchange` plugin) are running locally, per `CLAUDE.md`.

- [ ] **Step 3: Run the services**

In separate terminals:
```bash
dotnet run --project src/WebApp
dotnet run --project src/OrderSaga
dotnet run --project src/InventoryService
dotnet run --project src/PaymentService
dotnet run --project src/NotificationService
```

- [ ] **Step 4: Create an order and observe saga progression**

Via WebApp's Swagger UI (or by enabling `OrderSimulator:Enabled` in `src/WebApp/appsettings.json` briefly), create an order. Expected:
- A row appears in SQL Server's `OrderSagaStates` table with `CurrentState` starting at `CheckingInventory`.
- Corresponding rows appear in `SagaOrderDetails` for the order's line items.
- Within a few seconds, `CurrentState` progresses to `ProcessingPayment` then `Confirmed` (assuming `HasSufficientStockAsync` returns true and payment stub always succeeds per `CLAUDE.md`'s documented stubs).
- No exceptions logged referencing Mongo saga persistence.

- [ ] **Step 5: Verify `SagaDashboard`**

```bash
dotnet run --project src/SagaDashboard
```
Navigate to `/OrderSagas`. Expected: the order created in Step 4 appears in the list with correct `OrderId`/`CustomerName`/`OrderDate`/`TotalAmount`/`CurrentState`. Filtering by `orderId` and by `state` both work.

- [ ] **Step 6: No commit needed — this task is verification only**

If any step fails, return to the relevant earlier task, fix, rebuild, and re-verify from Step 1.
