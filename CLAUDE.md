# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & run

```
dotnet build MicroService.sln
dotnet run --project src/WebApp            # HTTP API + Swagger + MassTransit dashboard
dotnet run --project src/OrderSaga         # saga orchestrator (worker, no HTTP)
dotnet run --project src/InventoryService  # worker
dotnet run --project src/PaymentService    # worker
dotnet run --project src/NotificationService # worker
dotnet run --project src/RabbitMqCleanupTool -- --yes   # deletes this solution's queues/exchanges (dry-run without --yes)
```

No test project exists in the solution currently.

EF Core migrations live in `src/Infrastructure/Migrations`, model owned by `AppDbContext` (`src/Infrastructure/Persistence/AppDbContext.cs`). Design-time factory: `AppDbContextFactory`. Run migration commands with `--project src/Infrastructure --startup-project src/WebApp`.

Requires locally: SQL Server (OrderDB), MongoDB (OrderSagaDb — saga state + Serilog logs), RabbitMQ with the `rabbitmq_delayed_message_exchange` plugin enabled (required by `UseDelayedMessageScheduler`/`UseDelayedRedelivery`). Connection strings/credentials are in each service's `appsettings.json` (all point at `localhost`, shared dev credentials).

## Architecture

This is an order-processing system built as a MassTransit saga across several .NET worker/host services, all sharing one solution and one set of Clean-Architecture class libraries.

**Shared libraries** (referenced by every executable project):
- `Domain` — entities (`Order`, `OrderDetail`, `Product`), repository interfaces, `IUnitOfWork`
- `Infrastructure` — EF Core (`AppDbContext`, SQL Server) + Mongo client registration, repository implementations. `DependencyInjection.AddInfrastructure()` wires both DBs.
- `Application` — `IOrderService`/`IProductService` + implementations, AutoMapper profile. `DependencyInjection.AddApplication()` wires these.
- `Application.Dtos` — DTOs crossing the WebApp API boundary (`OrderDto`, `ProductDto`, `OrderDetailDto`)
- `Application.Messaging` — MassTransit message contracts shared by every service: commands (`CheckInventory`, `ProcessPayment`) and events (`OrderCreated`, `InventoryChecked`, `PaymentProcessed`, `OrderConfirmed`). This is the only coupling between services — they never reference each other directly, only these contracts.

**Executables** (each is its own MassTransit bus host, `AddInfrastructure()` + `AddApplication()` + `AddMassTransit()`):
- `WebApp` — ASP.NET Core API (`OrderController`), publish-only bus (EF outbox pattern, no consumers), Swagger, MassTransit dashboard (`/`), plus `OrderSimulatorService` background service that generates test orders.
- `OrderSaga` — hosts `OrderStateMachine` (MassTransit state machine), the orchestrator. Saga state (`OrderSagaState`) persisted in MongoDB (`MongoDbRepository`), not SQL. Ensures the Mongo saga collection and a Serilog TTL index exist on startup (`SerilogRetentionSetup`).
- `InventoryService` — worker owning `inventory-queue`; `InventoryConsumer` handles `CheckInventory`, publishes `InventoryChecked`. This is the only service with a manually configured `ReceiveEndpoint` (retry, delayed-redelivery, and per-`CorrelationId` partitioner — see comments in `Program.cs` before changing message types handled here).
- `PaymentService` — worker; `PaymentConsumer` handles `ProcessPayment` → publishes `PaymentProcessed` (payment logic is currently a stub, always succeeds).
- `NotificationService` — worker; `NotificationConsumer` handles `OrderConfirmed` (stubbed, logs only).
- `RabbitMqCleanupTool` — standalone console utility (not a bus host) using the RabbitMQ Management HTTP API to dry-run/delete this solution's queues and exchanges by name pattern.

**Saga flow** (`src/OrderSaga/Saga/OrderStateMachine.cs`):
`OrderCreated → CheckingInventory → ProcessingPayment → Confirmed`, with `Failed` as a terminal dead-end. Two distinct retry mechanisms exist and are not interchangeable:
- Transport-level (`UseMessageRetry` / `UseDelayedRedelivery` in `InventoryService/Program.cs`) — only fires on unhandled exceptions/faults.
- Business-level (`Schedule<OrderSagaState, CheckInventory> InventoryRetry` in the state machine) — polls when the reply says "not available yet" (no exception), exponential backoff x5 starting at 1 min, capped at 1 day/step, gives up after 7 days total (`MaxRetryWindow`).

Consumers other than `InventoryConsumer`'s own service are marked `[ExcludeFromConfigureEndpoints]` and each service calls `cfg.ConfigureEndpoints(ctx)` anyway — this registers all consumers/saga for MassTransit dashboard visibility across services without creating duplicate queue bindings. When adding a new consumer to a queue, follow the `InventoryService/Program.cs` pattern (manual `ReceiveEndpoint`) if it needs custom retry/redelivery/partitioning; otherwise a default endpoint via `AddConsumer` + `ConfigureEndpoints` is enough for simple pass-through consumers like `PaymentConsumer`/`NotificationConsumer`.

All bus JSON (de)serialization uses Newtonsoft, not `System.Text.Json` (`UseNewtonsoftJsonSerializer`/`Deserializer`) — keep this consistent when adding new message contracts.

Serilog is configured entirely from each service's `appsettings.json` (`Serilog` section), writing to MongoDB (`Serilog.Sinks.MongoDB`), with a TTL index (`SerilogRetentionSetup`) enforcing retention — currently only wired up in `OrderSaga`. Note: `SerilogRetentionSetup.EnsureSerilogTtlIndex` builds the TTL via `TimeSpan.FromMinutes(retentionDays)` (`src/OrderSaga/SerilogRetentionSetup.cs:33`) — the `retentionDays` param is actually applied in minutes, not days, despite the name/doc comment.

## Order creation & the EF outbox

Orders are created two ways, both going through the same pattern (`OrderService.CreateAsync` in `src/Application/Services/OrderService.cs`, and `WebApp/Services/OrderSimulatorService.cs` — a `BackgroundService` that fabricates a random order every `OrderSimulator:IntervalSeconds` seconds, controlled by `OrderSimulator:Enabled` in config, off by default if products table is empty):
1. Insert the `Order`/`OrderDetail` rows and `SaveChangesAsync` so the DB assigns `order.Id`.
2. `bus.Publish(new OrderCreated { Order = ... })` — MassTransit's EF outbox intercepts this and writes an `OutboxMessage` row in the same DbContext instead of hitting RabbitMQ directly.
3. A second `SaveChangesAsync` + transaction commit persists both the order and the outbox row atomically.
4. MassTransit's outbox delivery service later forwards the message to RabbitMQ — so a crash between steps 1 and 3 loses nothing, and a crash after commit still delivers once the process is back up.

`ProductService.ReduceStockQtyAsync` exists but is not currently called anywhere (`PaymentConsumer` has the call commented out) — stock is only read (`HasSufficientStockAsync`), never decremented, so `Product.Stock` never actually depletes from orders today.

## Known gaps / stubs (as of current code)

- `PaymentConsumer` (`src/PaymentService/PaymentConsumer.cs`): payment logic is `var isSuccess = true;` — no real processing.
- `NotificationConsumer` (`src/NotificationService/NotificationConsumer.cs`): logs only, no email/push.
- `OrderStateMachine`'s `OrderConfirmed` publish (`src/OrderSaga/Saga/OrderStateMachine.cs:152`) only sets `CorrelationId`/`OrderId` — the event's `CustomerName`/`TotalAmount` fields are left default, even though `NotificationConsumer` logs them.
- `InventoryConsumer` has a hardcoded simulated failure for `ProductId == 1` (throws `HttpRequestException` to exercise the retry/redelivery path) — remove/gate this before relying on inventory checks for a real product with id 1.
