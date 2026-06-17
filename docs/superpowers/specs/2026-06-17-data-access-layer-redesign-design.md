# Data Access Layer Redesign — Clean Architecture + Repository/UoW

## Goal

Redesign the data access layer of the order-processing microservice solution to apply SOLID, Repository Pattern, Unit of Work Pattern, and Clean Architecture layering — without changing observable behavior (API contracts, outbox/saga semantics, message flow).

## Scope

In scope: `WebApp`'s order CRUD path (`OrderController` → `AppDbContext` direct access today). This is the only place in the solution doing real database reads/writes outside MassTransit-owned persistence.

Out of scope (confirmed, no real DB access exists there today):
- `OrderSaga` — saga state persistence is owned by MassTransit's `EntityFrameworkRepository<OrderSagaState>`. Not wrapped in custom repository; it's a framework contract, not application data access.
- `InventoryConsumer`, `PaymentConsumer`, `NotificationConsumer` — all stub logic (`// TODO: real ... logic`), no DB calls today. They become consumers of the new `Infrastructure` library once their logic is implemented (future work, not this change).

## Current State (baseline)

- `Db.Repository` project: `AppDbContext`, entities (`Order`, `OrderDetail`, `Product`, `OrderSagaState`), migrations, design-time factory.
- `WebApp/Controllers/OrderController.cs`: injects `AppDbContext` directly, does EF queries inline, manages transaction + outbox flush manually around `bus.Publish`.
- No service/use-case layer; no repository abstraction; no unit tests possible without a real DB context.

## Target Architecture

New projects added to `MicroService.sln`:

```
src/Domain/
  Entities/        Order, OrderDetail, Product, OrderSagaState  (moved from Db.Repository)
  Repositories/     IGenericRepository<T>, IOrderRepository, IProductRepository
  IUnitOfWork.cs
  Exceptions/       EntityNotFoundException, etc.

src/Application/
  Interfaces/        IOrderService
  Services/          OrderService
  Mappings/          MapperProfile (moved from WebApp/Mappings)

src/Infrastructure/
  Persistence/        AppDbContext, EF entity configurations (IEntityTypeConfiguration<T> per entity)
  Repositories/        GenericRepository<T>, OrderRepository, ProductRepository
  UnitOfWork.cs
  Migrations/           (moved from Db.Repository)
  DependencyInjection.cs  (AddInfrastructure(IServiceCollection, IConfiguration) extension)
```

`Db.Repository` project is retired — its contents redistribute to `Domain` (entities) and `Infrastructure` (DbContext, migrations, design-time factory). `Contracts` project keeps its existing `Dto/` folder (`OrderDto`, `OrderDetailDto`, `ProductDto`) as-is — these are cross-service message payloads (`OrderCreated.Order` is an `OrderDto`), not WebApp-only, so `Application` references `Contracts` for them rather than owning a copy. Messages, saga state machine, consumer stubs in `Contracts` are otherwise untouched.

### Repository contracts (Domain layer)

```csharp
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(int id);
    Task<IReadOnlyList<T>> GetAllAsync();
    Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task AddAsync(T entity);
    void Update(T entity);
    void Remove(T entity);
}

public interface IOrderRepository : IGenericRepository<Order>
{
    Task<Order?> GetByIdWithDetailsAsync(int id);
    Task<IReadOnlyList<Order>> GetAllWithDetailsAsync();
}

public interface IProductRepository : IGenericRepository<Product>
{
    Task<bool> HasSufficientStockAsync(int productId, int qty);
}

public interface IUnitOfWork : IAsyncDisposable
{
    IOrderRepository Orders { get; }
    IProductRepository Products { get; }
    Task<int> SaveChangesAsync();
    Task BeginTransactionAsync();
    Task CommitAsync();
    Task RollbackAsync();
}
```

`GetByIdWithDetailsAsync`/`GetAllWithDetailsAsync` encapsulate the `Include(o => o.OrderDetails).ThenInclude(d => d.Product)` query shape currently inline in the controller. `HasSufficientStockAsync` is added for future InventoryService real-logic use (not called by current flow, but establishes the contract now since Product repo is being introduced anyway).

### Order.Create flow — preserving outbox atomicity

The current controller does:
1. `BeginTransactionAsync`
2. `db.Orders.Add(order)` + `SaveChangesAsync` (assigns `order.Id`)
3. `bus.Publish(OrderCreated)` (writes outbox row, same DbContext)
4. `SaveChangesAsync` (flushes outbox row)
5. `CommitAsync`

This exact sequence moves into `Application.Services.OrderService.CreateOrderAsync`, using `IUnitOfWork` + `IPublishEndpoint`:

```csharp
public async Task<OrderDto> CreateOrderAsync(OrderDto request)
{
    var order = mapper.Map<Order>(request);
    order.OrderDate = DateTime.UtcNow;
    foreach (var d in order.OrderDetails) d.Total = d.OrderQty * d.UnitPrice;
    order.TotalAmount = order.OrderDetails.Sum(d => d.Total);

    await uow.BeginTransactionAsync();
    await uow.Orders.AddAsync(order);
    await uow.SaveChangesAsync();                                  // 1) Id assigned

    await bus.Publish(new OrderCreated { Order = mapper.Map<OrderDto>(order) });
    await uow.SaveChangesAsync();                                  // 2) flush outbox row

    await uow.CommitAsync();
    return mapper.Map<OrderDto>(order);
}
```

`IPublishEndpoint` is injected into `OrderService` (Application layer), not into `Infrastructure` — keeps messaging concerns out of the data-access layer. `UnitOfWork.SaveChangesAsync` is just `AppDbContext.SaveChangesAsync` underneath; `BeginTransactionAsync`/`CommitAsync` wrap `Database.BeginTransactionAsync()`/`tx.CommitAsync()`.

`OrderController` shrinks to:

```csharp
[HttpPost]
public async Task<IActionResult> Create(OrderDto request)
{
    var result = await orderService.CreateOrderAsync(request);
    return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
}
```

Same transformation applies to `GetAll`, `GetById`, `Update`, `Delete` — each becomes a thin call into `IOrderService`, business/query logic moves to `OrderService` + repositories.

### EF Core configuration

Inline `OnModelCreating` blocks in `AppDbContext` split into `IEntityTypeConfiguration<T>` classes per entity (`ProductConfiguration`, `OrderConfiguration`, `OrderDetailConfiguration`, `OrderSagaStateConfiguration`), applied via `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly)`. Outbox/inbox entity registration (`AddInboxStateEntity()` etc.) stays inline in `OnModelCreating` since it's MassTransit's own extension method, not a candidate for `IEntityTypeConfiguration`.

### Dependency injection

`Infrastructure.DependencyInjection.AddInfrastructure(IServiceCollection, IConfiguration)`:
- registers `AppDbContext` (same `UseSqlServer` + connection string lookup as today)
- registers `IUnitOfWork → UnitOfWork`, `IOrderRepository → OrderRepository`, `IProductRepository → ProductRepository` (scoped)

`Application` gets its own `AddApplication(IServiceCollection)` registering `IOrderService → OrderService` and the AutoMapper profile.

`WebApp/Program.cs` calls `builder.Services.AddInfrastructure(builder.Configuration); builder.Services.AddApplication();` in place of today's inline `AddDbContext` + `AddAutoMapper` calls. MassTransit's own `AddEntityFrameworkOutbox<AppDbContext>` registration is unaffected — it still points at the same `AppDbContext` type, now sourced from `Infrastructure`.

### EF migrations

Migrations move from `Db.Repository/Migrations` to `Infrastructure/Migrations`. `dotnet ef` commands in `CLAUDE.md` update from `--project src/Db.Repository` to `--project src/Infrastructure`. No schema change — same `AppDbContext`, same model, migrations are a straight relocation (regenerated against the new project to keep the designer-file project reference correct, not a new migration with schema changes).

## SOLID mapping

- **SRP** — repository = data access only; service = business rule/orchestration; controller = HTTP concern only.
- **OCP** — new entity needs a new repository interface/implementation; existing repositories/services untouched.
- **LSP** — anywhere `IGenericRepository<T>` is expected, any concrete repository for that T substitutes safely.
- **ISP** — `IOrderRepository`/`IProductRepository` expose only what their callers need; generic members don't leak entity-specific query shapes onto callers that don't need them.
- **DIP** — `OrderController`/`OrderService` depend on `IOrderService`/`IUnitOfWork` abstractions; concrete `OrderService`/`UnitOfWork`/EF repositories are wired via DI in `Program.cs`.

## Testability gain

`OrderService` becomes unit-testable with mocked `IUnitOfWork` + `IPublishEndpoint` — no SQL Server or RabbitMQ required for business-logic tests. (No test project exists in the solution yet per `CLAUDE.md`; this change makes adding one straightforward but does not itself add a test project — separate decision if wanted later.)

## Non-goals / explicitly preserved behavior

- API routes, request/response DTOs, HTTP status codes — unchanged.
- Outbox atomicity (the two-`SaveChangesAsync`-calls-around-`Publish`-inside-one-transaction pattern) — preserved exactly, just relocated into `OrderService`.
- `AddAllConsumers()` shared-topology pattern, saga state machine, consumer definitions — untouched.
- Newtonsoft JSON serialization on the bus — untouched.
- `net10.0` target, MassTransit version — untouched.

## Risk notes

- Moving `AppDbContext` to a new project changes the assembly migrations live in. EF Core migrations embed a model snapshot tied to the assembly; moving requires regenerating the migration (same schema, new project) rather than a raw file copy, to avoid a mismatched designer/snapshot assembly reference.
- Confirmed: all 5 services (`WebApp`, `OrderSaga`, `InventoryService`, `PaymentService`, `NotificationService`) construct `AppDbContext` directly in `Program.cs` for `AddEntityFrameworkOutbox<AppDbContext>`/`UseEntityFrameworkOutbox<AppDbContext>`/saga repository — every service has its own outbox table backed by the same `AppDbContext`. `Contracts.csproj` also references `Db.Repository` (saga state machine needs `OrderSagaState`). This means `Infrastructure` (replacing `Db.Repository`) is a dependency of *every* project in the solution, not just `WebApp`. All `using Db.Repository;` → `using Infrastructure.Persistence;` (or wherever `AppDbContext`/entities land) and all `ProjectReference`s to `Db.Repository.csproj` must be repointed to `Infrastructure.csproj` and (for entities) `Domain.csproj` in the same change, or the solution fails to build entirely.
