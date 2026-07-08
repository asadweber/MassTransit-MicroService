using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;

namespace OrderSaga.Saga;

/// <summary>
/// Order-processing saga: OrderCreated -> CheckingInventory -> ProcessingPayment -> Confirmed,
/// with a Failed dead-end on inventory-unavailable-past-window or payment failure.
/// Persisted via EF Core (<see cref="OrderSagaState"/>) so state survives process restarts.
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderSagaState>
{
    // Give up polling inventory after 7 days of continuous unavailability.
    public static readonly TimeSpan MaxRetryWindow = TimeSpan.FromDays(7);
  
    // Delay before the first inventory re-check.
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);
    
    // Ceiling for any single backoff step, however large BackoffFactor grows it.
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);
    
    // Multiplier applied per retry attempt (1m, 5m, 25m, 125m, ...).
    private const int BackoffFactor = 5;

    // Waiting on InventoryChecked after publishing CheckInventory.
    public State CheckingInventory { get; private set; } = null!;
    
    // Waiting on PaymentProcessed after publishing ProcessPayment.
    public State ProcessingPayment { get; private set; } = null!;
    
    // Terminal success state; saga finalizes here.
    public State Confirmed { get; private set; } = null!;
    
    // Terminal failure state (inventory exhausted retries, or payment declined).
    public State Failed { get; private set; } = null!;

    // Starts a new saga instance.
    public Event<OrderCreated> OrderCreated { get; private set; } = null!;
    
    // Reply from InventoryService indicating stock availability.
    public Event<InventoryChecked> InventoryChecked { get; private set; } = null!;
    
    // Reply from PaymentService indicating charge outcome.
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;

    // Delayed self-message used to re-poll inventory without blocking the consumer.
    public Schedule<OrderSagaState, CheckInventory> InventoryRetry { get; private set; } = null!;

    private readonly ILogger<OrderStateMachine> _logger;

    public OrderStateMachine(ILogger<OrderStateMachine> logger)
    {
        _logger = logger;

        InstanceState(x => x.CurrentState);

        // First event for a saga instance: correlate by OrderId (no CorrelationId exists yet)
        // and mint a new one. All later events correlate by that generated CorrelationId.
        Event(() => OrderCreated, x =>
            x.CorrelateBy((state, ctx) => state.Order == ctx.Message.Order)
             .SelectId(_ => NewId.NextGuid()));

        Event(() => InventoryChecked, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => PaymentProcessed, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        // Business-level retry for "not available yet" (no exception thrown), distinct from
        // transport-level UseMessageRetry/UseDelayedRedelivery which only handle faulted messages.
        Schedule(() => InventoryRetry, x => x.InventoryRetryTokenId, x =>
        {
            x.Delay = FirstRetryDelay;
            x.Received = r => r.CorrelateById(ctx => ctx.Message.CorrelationId);
        });

        // New order: record it on the saga, ask InventoryService to check stock, move on.
        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    ctx.Saga.Order = ctx.Message.Order;
                    Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Saga.CorrelationId);
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.Order.Id);
                })
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ctx.Saga.Order,
                }))
                .TransitionTo(CheckingInventory)
                .Then(ctx => _logger.LogInformation("OrderCreated -> CheckingInventory")));

        // Handle the three ways CheckingInventory can resolve: available now, still
        // unavailable (retry or give up), or a scheduled retry firing.
        During(CheckingInventory,
            When(InventoryChecked, x => x.Message.IsAvailable)
                .Unschedule(InventoryRetry)
                .Then(ctx =>
                {
                    ctx.Saga.FirstUnavailableAt = null;
                    ctx.Saga.InventoryRetryCount = 0;
                    Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Saga.CorrelationId);
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.Order.Id);
                })
                .PublishAsync(ctx => ctx.Init<ProcessPayment>(new ProcessPayment
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ctx.Saga.Order,
                }))
                .TransitionTo(ProcessingPayment)
                .Then(ctx => _logger.LogInformation("InventoryChecked (available) -> ProcessingPayment")),

            // Still unavailable: give up only once MaxRetryWindow (7d from first-seen-unavailable)
            // has elapsed; otherwise schedule another check with growing backoff.
            When(InventoryChecked, x => !x.Message.IsAvailable)
                .IfElse(ctx => IsRetryWindowExpired(ctx.Saga),
                    stillUnavailable => stillUnavailable
                        .TransitionTo(Failed)
                        .Then(ctx => _logger.LogWarning(
                            "Order {OrderId} [{CorrelationId}]: InventoryChecked (unavailable, retry window expired) -> Failed",
                            ctx.Saga.Order.Id, ctx.Saga.CorrelationId)),
                    retry => retry
                        .Then(ctx =>
                        {
                            ctx.Saga.FirstUnavailableAt ??= DateTime.UtcNow;
                            ctx.Saga.InventoryRetryCount++;
                        })
                        .Schedule(InventoryRetry,
                            ctx => ctx.Init<CheckInventory>(new CheckInventory
                            {
                                CorrelationId = ctx.Saga.CorrelationId,
                                Order = ctx.Saga.Order,
                            }),
                            ctx => GetRetryDelay(ctx.Saga.InventoryRetryCount))
                        .Then(ctx => _logger.LogInformation(
                            "Order {OrderId} [{CorrelationId}]: InventoryChecked (unavailable) -> retry #{RetryCount} scheduled",
                            ctx.Saga.Order.Id, ctx.Saga.CorrelationId, ctx.Saga.InventoryRetryCount))),

            // Fires when the scheduled delay elapses (token stored via InventoryRetryTokenId) —
            // re-publish CheckInventory to poll availability again. Reads ctx.Saga.Order (not
            // ctx.Message) so an admin edit to the order's line items while stuck retrying is
            // picked up on the next check, instead of re-checking the stale scheduled payload.
            When(InventoryRetry.Received)
                .Then(ctx => _logger.LogInformation(
                    "Order {OrderId} [{CorrelationId}]: InventoryRetry fired, re-checking inventory",
                    ctx.Saga.Order.Id, ctx.Saga.CorrelationId))
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ctx.Saga.Order,
                })));

        // Payment resolves the saga: success confirms and finalizes, failure ends in Failed.
        During(ProcessingPayment,
            When(PaymentProcessed, x => x.Message.IsSuccess)
                .PublishAsync(ctx => ctx.Init<OrderConfirmed>(new OrderConfirmed
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ctx.Saga.Order,
                }))
                .TransitionTo(Confirmed)
                .Then(ctx => _logger.LogInformation(
                    "Order {OrderId} [{CorrelationId}]: PaymentProcessed (success) -> Confirmed",
                    ctx.Saga.Order.Id, ctx.Saga.CorrelationId))
                .Finalize(),

            When(PaymentProcessed, x => !x.Message.IsSuccess)
                .TransitionTo(Failed)
                .Then(ctx => _logger.LogWarning(
                    "Order {OrderId} [{CorrelationId}]: PaymentProcessed (declined) -> Failed",
                    ctx.Saga.Order.Id, ctx.Saga.CorrelationId)));

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Exponential backoff (x5 per attempt: 1m, 5m, 25m, 125m, ...), capped at <see cref="MaxRetryDelay"/> per step.
    /// </summary>
    private static TimeSpan GetRetryDelay(int retryCount)
    {
        var delayMinutes = FirstRetryDelay.TotalMinutes * Math.Pow(BackoffFactor, retryCount - 1);
        var delay = TimeSpan.FromMinutes(delayMinutes);
        return delay > MaxRetryDelay ? MaxRetryDelay : delay;
    }

    private static bool IsRetryWindowExpired(OrderSagaState saga)
    {
        var firstUnavailableAt = saga.FirstUnavailableAt ?? DateTime.UtcNow;
        return DateTime.UtcNow - firstUnavailableAt >= MaxRetryWindow;
    }
}
