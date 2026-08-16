# Saga Persistence Migration: MongoDB → SQL Server (EF Core)

## Goal

Replace `OrderStateMachine`'s MongoDB-backed saga persistence (`OrderSagaState` via MassTransit's `MongoDbRepository`) with SQL Server via EF Core, using the existing `AppDbContext` / OrderDB. Keep `SagaDashboard` working against the new store.

## Scope

- `OrderSaga` — swap `.MongoDbRepository(...)` for `.EntityFrameworkRepository(...)`.
- `Infrastructure` — extend `AppDbContext` with saga state + saga order-detail tables, add EF configuration, add migration.
- `SagaDashboard` — rewrite `OrderSagasController` and `Views/OrderSagas/Index.cshtml` to query `AppDbContext` instead of `IMongoCollection<OrderSagaState>`.
- `OrderStateMachine` / `OrderSagaDefinition` — fix `CorrelateBy` (currently compares whole `OrderDto` by object equality) to compare the flattened `OrderId` instead.
- Mongo (`OrderSagaDb`) stays in use for Serilog logs only — `SerilogRetentionSetup` is untouched. Only saga *state* persistence moves.

Out of scope: `ProductService.ReduceStockQtyAsync`, payment/notification stubs, retry-window tuning — unrelated to persistence swap.

## Schema changes

`OrderSagaState` currently carries a nested `OrderDto` (with a `List<OrderDetailDto>`). EF/SQL Server needs this flattened:

```csharp
public class OrderSagaState : SagaStateMachineInstance, ISagaVersion
{
    public Guid CorrelationId { get; set; }
    public string CurrentState { get; set; } = string.Empty;
    public int Version { get; set; }               // EF concurrency token

    // Flattened from OrderDto
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

public class SagaOrderDetail
{
    public int Id { get; set; }
    public Guid OrderSagaStateCorrelationId { get; set; }  // FK
    public int ProductId { get; set; }
    public int OrderQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Total { get; set; }
}
```

`SagaOrderDetail` is a plain owned/dependent table (EF owned collection, keyed by `CorrelationId`), not a reference to `Domain.OrderDetail` — it's a point-in-time copy carried through the saga, same as today's `OrderDetailDto` copy embedded in Mongo.

`AppDbContext` gains:
```csharp
public DbSet<OrderSagaState> OrderSagaStates => Set<OrderSagaState>();
```
configured via `OrderSagaStateConfiguration : IEntityTypeConfiguration<OrderSagaState>` (co-located with other configs, picked up by `ApplyConfigurationsFromAssembly`):
- `HasKey(x => x.CorrelationId)`
- `Property(x => x.Version).IsConcurrencyToken()`
- `OwnsMany(x => x.OrderDetails, ...)` mapped to a `SagaOrderDetails` table with `WithOwner().HasForeignKey(...)`

New migration `AddOrderSagaState`, generated against `src/Infrastructure --startup-project src/WebApp` per existing convention.

## Correlation fix

`OrderStateMachine.cs:73`:
```csharp
x.CorrelateBy((instance, context) => instance.Order == context.Message.Order)
```
This compares a nested DTO by reference/structural equality — already fragile, and breaks outright once `Order` is flattened (no single field to compare against `context.Message.Order`, a DTO). Change to:
```csharp
x.CorrelateBy((instance, context) => instance.OrderId == context.Message.Order.Id)
```
All other `ctx.Saga.Order.Id` / `ctx.Saga.Order` references in `OrderStateMachine.cs` and `OrderSagaDefinition.cs:91` update to `ctx.Saga.OrderId` (scalar reads) or construct an `OrderDto` inline from the flattened fields where a full DTO is published in an event (e.g. `CheckInventory { Order = ... }`).

## Repository wiring (`OrderSaga/Program.cs`)

```csharp
builder.Services.AddInfrastructure(builder.Configuration); // already adds AppDbContext

builder.Services.AddMassTransit(x =>
{
    x.AddSagaStateMachine<OrderStateMachine, OrderSagaState, OrderSagaDefinition>()
        .EntityFrameworkRepository(r =>
        {
            r.ExistingDbContext<AppDbContext>();
            r.ConcurrencyMode = ConcurrencyMode.Optimistic;
        });
    ...
});
```
`OrderSaga.csproj` gains a project reference to `Infrastructure` (it currently only references `Application`/`Application.Dtos`/`Application.Messaging`). The Mongo collection-existence bootstrap block in `Program.cs` (lines 58-76) is deleted — no longer needed, EF migration handles table creation (`dotnet ef database update` as part of deploy, same as WebApp's existing convention — no auto-migrate-on-startup code exists today, so none is added here).

`mongoSection`/`MongoDb:*` config stays only for the Serilog TTL setup — saga-collection-specific settings (`MongoDb:SagaCollection`) are removed from `appsettings.json` once unused there.

## SagaDashboard rewrite

`OrderSagasController` currently injects `IMongoCollection<OrderSagaState>` and does raw Mongo filters/updates. Rewrite to inject `AppDbContext`, replacing:
- `_sagas.Find(...)` → `_db.OrderSagaStates.Include(s => s.OrderDetails).Where(...)`
- `Builders<OrderSagaState>.Update.Set(s => s.Order, order)` (line 109 — a manual "fix stuck saga" admin action) → direct property assignment + `SaveChangesAsync()`

`Program.cs`'s `.MongoDbRepository(...)` for `DashboardOnlySagaDefinition` becomes `.EntityFrameworkRepository(...)` identically to `OrderSaga/Program.cs`. `Index.cshtml` binds to the same `OrderSagaState` shape, only `@Model.Order.CustomerName` etc. become `@Model.CustomerName` (flattened) — view logic otherwise unchanged.

`SagaDashboard.csproj` gains reference to `Infrastructure` (likely already present via other consumers — verify during implementation).

## Testing

No test project exists in the solution (per CLAUDE.md). Verification is manual: run migration, start `OrderSaga` + `WebApp`, create an order via Swagger/simulator, confirm a row appears in the new SQL tables and the saga progresses through `CheckingInventory → ProcessingPayment → Confirmed`, then confirm `SagaDashboard` renders it correctly.

## Rollback consideration

This is a one-way schema/behavior change — no dual-write or fallback to Mongo saga state is implemented (YAGNI; this is a dev-stage migration per CLAUDE.md's "no test project" / single-shared-creds posture, not a live production cutover with zero-downtime requirements). Existing in-flight sagas in MongoDB are abandoned, not migrated — acceptable since this is understood to be a dev/pre-production environment.
