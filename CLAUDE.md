# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

Build / run (from repo root, `MicroService.sln`):

```
dotnet build
dotnet run --project src/WebApp
dotnet run --project src/OrderSaga
dotnet run --project src/InventoryService
dotnet run --project src/PaymentService
dotnet run --project src/NotificationService
```

EF Core migrations (always specify both projects — `Infrastructure` has no startup project of its own):

```
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/WebApp
dotnet ef database update --project src/Infrastructure --startup-project src/WebApp
```

Infra dependencies (not containerized in this repo — must be running locally):
- SQL Server on `localhost`, database `OrderDB` (see `ConnectionStrings:DefaultConnection` in each service's `appsettings.json`)
- RabbitMQ on `localhost`, default guest/guest vhost `/` (see `RabbitMQ` section in `appsettings.json`)

No test project exists in the solution yet.

## Architecture

This is a MassTransit/RabbitMQ saga-based order-processing system targeting **net10.0**, using `MassTransit 9.2.0-develop.150` (RabbitMQ + EF Core saga repository + EF Core transactional outbox/inbox throughout).

### Projects (`src/`)
- **WebApp** — ASP.NET Core API + Swagger. Publishes `OrderCreated` only; does **not** consume anything (publish-only bus, no `ConfigureEndpoints` queue creation). Hosts the MassTransit dashboard (`/`) and a background `OrderSimulatorService` that creates random orders on an interval (toggle via `OrderSimulator:Enabled`/`IntervalSeconds` in appsettings).
- **OrderSaga** — worker host that owns the `OrderStateMachine` saga (`Contracts/Saga/OrderStateMachine.cs`), persisted via `EntityFrameworkRepository` against `AppDbContext`.
- **InventoryService**, **PaymentService**, **NotificationService** — each is a worker host that owns exactly one consumer (`InventoryConsumer`, `PaymentConsumer`, `NotificationConsumer` respectively) and declares a manual `ReceiveEndpoint` for its queue (`inventory-queue`, `payment-queue`, `notification-queue`).
- **Contracts** — shared message contracts (`Messages/`, `Messages/Events/`), saga state machine + definition (`Saga/`), all consumer implementations and their `IConsumerDefinition`s (`Consumers/`), DTOs (`Dto/`), and the `BusTopologyExtensions.AddAllConsumers` helper.
- **Domain** — entities (`Order`, `OrderDetail`, `Product`, `OrderSagaState`), repository contracts (`IGenericRepository<T>`, `IOrderRepository`, `IProductRepository`), `IUnitOfWork`, domain exceptions. Referenced by every project that needs entity types (directly or transitively).
- **Application** — `IOrderService`/`OrderService` (orchestrates `IUnitOfWork` + `IPublishEndpoint` + AutoMapper for order CRUD, preserving the original outbox transaction sequence), AutoMapper profile, `AddApplication` DI extension. Referenced by `WebApp`.
- **Infrastructure** — `AppDbContext` (now at `Infrastructure.Persistence.AppDbContext`), EF entity type configurations (`IEntityTypeConfiguration<T>` per entity), concrete repository implementations, `UnitOfWork`, EF Core migrations, `AddInfrastructure` DI extension. Referenced by every service (`WebApp`, `OrderSaga`, `InventoryService`, `PaymentService`, `NotificationService`) since each owns its own MassTransit EF Core outbox table requiring `AppDbContext`.

### The shared-topology pattern (important, non-obvious)

Every service calls `AddAllConsumers()` (`src/Contracts/BusTopologyExtensions.cs`), which registers **all** consumers and the saga in every process — not just the ones that process belongs to. This is intentional: it lets the MassTransit dashboard in WebApp see the full message topology/flow diagram across services.

- The service that actually owns a consumer passes `ownerConsumerType: typeof(XConsumer)`; every other service gets that consumer registered but immediately `ExcludeFromConfigureEndpoints()`'d, so no queue is created for it there.
- The saga is handled the same way: pass `configureSagaRepository` (only `OrderSaga` does) to wire up the EF saga repository; everyone else excludes it from endpoint configuration.
- When adding a new consumer: register it inside `AddAllConsumers`, give it an `IConsumerDefinition`, and have its owning service pass `ownerConsumerType` and declare a manual `ReceiveEndpoint` for the queue (mirroring `InventoryService/Program.cs`).

### Saga flow (`Contracts/Saga/OrderStateMachine.cs`)

`OrderCreated` → `CheckingInventory` → (`InventoryChecked`, available) → `ProcessingPayment` → (`PaymentProcessed`, success) → `Confirmed`/finalized. Any negative check (`!IsAvailable` / `!IsSuccess`) transitions to `Failed` (currently a dead-end state — no compensation/notification wired from `Failed` yet). Correlation: `OrderCreated` correlates by `state.OrderId == ctx.Message.Order.Id` and generates a new saga `CorrelationId`; subsequent events correlate by that `CorrelationId`.

### Outbox/Retry conventions

Every receive endpoint (saga and each owned consumer queue) is configured in the same order — this order matters and should be preserved when adding new endpoints:
1. `UseMessageRetry` (outermost — 5s/15s/30s intervals)
2. `UseEntityFrameworkOutbox<AppDbContext>` (atomic with the DB transaction)
3. `ConfigureConsumer<T>` (innermost)

WebApp's `OrderController.Create` shows the expected outbox usage pattern for publishers: begin a DB transaction, save the entity (to get its DB-assigned Id), `Publish` the event (writes to the outbox table within the same transaction), `SaveChangesAsync` again to flush the outbox row, then commit.

Message serialization is Newtonsoft JSON (`UseNewtonsoftJsonSerializer`/`Deserializer`) on every bus, not the MassTransit default System.Text.Json.
