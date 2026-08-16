using Application.Dtos;
using Application.Messaging.Command;
using Application.Messaging.Events;
using Infrastructure.Persistence;
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

    #region Saga State
    // Waiting on InventoryChecked after publishing CheckInventory.
    public State CheckingInventory { get; private set; } = null!;

    // Waiting on PaymentProcessed after publishing ProcessPayment.
    public State ProcessingPayment { get; private set; } = null!;

    // Terminal success state; saga finalizes here.
    public State Confirmed { get; private set; } = null!;

    // Terminal failure state (inventory exhausted retries, or payment declined).
    public State Failed { get; private set; } = null!;

    #endregion


    #region Saga Events

    // Starts a new saga instance.
    public Event<OrderCreated> OrderCreated { get; private set; } = null!;

    // Reply from InventoryService indicating stock availability.
    public Event<InventoryChecked> InventoryChecked { get; private set; } = null!;

    // Reply from PaymentService indicating charge outcome.
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;

    public Event<OrderConfirmedCompleted> OrderConfirmedCompleted{get;private set;} = default!;

    #endregion



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
            x.CorrelateBy((instance, context) => instance.OrderId == context.Message.Order.Id)
             .SelectId(_ => NewId.NextGuid()));

        Event(() => InventoryChecked, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => PaymentProcessed, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(
           () => OrderConfirmedCompleted,
           e =>
           {
               e.CorrelateById(context =>
                   context.Message.CorrelationId);

               e.OnMissingInstance(m =>
                   m.Discard());
           });

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
                    var order = ctx.Message.Order;
                    
                    var notification = order.OrderNotification;


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
                    ctx.Saga.OrderNotification = notification is null? null
                        : new SagaOrderNotification
                        {
                            OrderSagaStateCorrelationId = ctx.Saga.CorrelationId,
                            NotifyToEmail = notification.NotifyToEmail,
                            NotifyToSMS = notification.NotifyToSMS,
                            NotifyToPaci = notification.NotifyToPaci,
                            // Initial completion state
                            EmailSendStatus = notification.EmailSendStatus,
                            SMSSendStatus = notification.SMSSendStatus,
                            PaciSendStatus = notification.PaciSendStatus
                        };

                    Serilog.Context.LogContext.PushProperty("CorrelationId", ctx.Saga.CorrelationId);
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.OrderId);
                })
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ToOrderDto(ctx.Saga),
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
                    Serilog.Context.LogContext.PushProperty("OrderId", ctx.Saga.OrderId);
                })
                .PublishAsync(ctx => ctx.Init<ProcessPayment>(new ProcessPayment
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ToOrderDto(ctx.Saga),
                }))
                .TransitionTo(ProcessingPayment)
                .Then(ctx => _logger.LogInformation("InventoryChecked (available) -> ProcessingPayment")),

           // Still unavailable: give up only once MaxRetryWindow (7d from first-seen-unavailable)
           // has elapsed; otherwise schedule another check with growing backoff.
           When(InventoryChecked, x => !x.Message.IsAvailable)
                    .Then(ctx =>
                    {
                        // Record the first time inventory became unavailable.
                        ctx.Saga.FirstUnavailableAt ??= DateTime.UtcNow;                        
                    })
                    .IfElse(ctx => IsRetryWindowExpired(ctx.Saga),

                        // Retry window has expired.
                        expired => expired
                            .Unschedule(InventoryRetry)
                            .TransitionTo(Failed)
                            .Then(ctx => _logger.LogWarning(
                                "Order {OrderId} [{CorrelationId}]: Inventory unavailable for {RetryWindow}. Transitioning to Failed.",
                                ctx.Saga.OrderId,
                                ctx.Saga.CorrelationId,
                                MaxRetryWindow))
                             .Finalize(),

                        // Schedule another inventory check.
                        retry => retry
                            .Then(ctx =>
                            {
                                ctx.Saga.InventoryRetryCount++;

                                // Computed once and reused below so the persisted
                                // NextInventoryRetryAt always matches the actual
                                // scheduled fire time, even if the delay formula changes.
                                //var delay = GetRetryDelay(ctx.Saga.InventoryRetryCount);
                                ctx.Saga.NextInventoryRetryAt = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                            })
                            .Unschedule(InventoryRetry)
                            .Schedule(
                                InventoryRetry,
                                ctx => ctx.Init<CheckInventory>(new CheckInventory
                                {
                                    CorrelationId = ctx.Saga.CorrelationId,
                                    Order = ToOrderDto(ctx.Saga),
                                }),
                                ctx => ctx.Saga.NextInventoryRetryAt!.Value - DateTime.UtcNow)
                            .Then(ctx => _logger.LogInformation(
                                "Order {OrderId} [{CorrelationId}]: Inventory unavailable. Retry #{RetryCount} scheduled for {NextRetry}.",
                                ctx.Saga.OrderId,
                                ctx.Saga.CorrelationId,
                                ctx.Saga.InventoryRetryCount,
                                ctx.Saga.NextInventoryRetryAt))),

            // Fires when the scheduled delay elapses (token stored via InventoryRetryTokenId) —
            // re-publish as CheckInventory
            // can tell a saga-driven retry apart from the initial check. Reads saga state via
            // ToOrderDto(ctx.Saga) (not ctx.Message) so an admin edit to the order's line items while stuck retrying
            // is picked up on the next check, instead of re-checking the stale scheduled payload.
            When(InventoryRetry.Received)
                .Then(ctx => _logger.LogInformation(
                    "Order {OrderId} [{CorrelationId}]: InventoryRetry fired, re-checking inventory",
                    ctx.Saga.OrderId, ctx.Saga.CorrelationId))
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ToOrderDto(ctx.Saga),
                })),

            // A PaymentProcessed reply shouldn't be possible here (ProcessPayment isn't
            // published until CheckingInventory resolves), but a duplicate/late-redelivered
            // message can still land while the saga is mid-transition. Drop it instead of
            // throwing UnhandledEventException.
            Ignore(PaymentProcessed));

        // Payment resolves the saga: success confirms and finalizes, failure ends in Failed.
        During(ProcessingPayment,
            When(PaymentProcessed, x => x.Message.IsSuccess)
                .PublishAsync(ctx => ctx.Init<OrderConfirmed>(new OrderConfirmed
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    Order = ToOrderDto(ctx.Saga),
                }))
                .TransitionTo(Confirmed)
                .Then(ctx => _logger.LogInformation(
                    "Order {OrderId} [{CorrelationId}]: PaymentProcessed (success) -> Confirmed",
                    ctx.Saga.OrderId, ctx.Saga.CorrelationId))
                .Finalize(),

            When(PaymentProcessed, x => !x.Message.IsSuccess)
                .TransitionTo(Failed)
                .Then(ctx => _logger.LogWarning(
                    "Order {OrderId} [{CorrelationId}]: PaymentProcessed (declined) -> Failed",
                    ctx.Saga.OrderId, ctx.Saga.CorrelationId)),

            // Mirrors the CheckingInventory guard: a late/duplicate InventoryChecked or a
            // stale InventoryRetry firing after payment has already started shouldn't crash
            // the consumer — drop it instead of throwing UnhandledEventException.
            Ignore(InventoryChecked),
            Ignore(InventoryRetry.Received));

        // Late/duplicate redeliveries after the saga has already finalized or dead-ended
        // shouldn't crash the consumer — drop them.
        During(Failed,
            Ignore(InventoryChecked),
            Ignore(PaymentProcessed),
            Ignore(OrderCreated));

        SetCompletedWhenFinalized();
    }

    /// <summary>
    /// Exponential backoff (x5 per attempt: 1m, 5m, 25m, 125m, ...), capped at <see cref="MaxRetryDelay"/> per step.
    /// </summary>
    private  TimeSpan GetRetryDelay(int retryCount)
    {
        if (retryCount <= 1)
            return FirstRetryDelay;

        var delay = FirstRetryDelay;

        for (var i = 1; i < retryCount; i++)
        {
            if (delay >= MaxRetryDelay)
                return MaxRetryDelay;

            var nextTicks = delay.Ticks * BackoffFactor;

            // Prevent overflow
            if (nextTicks >= MaxRetryDelay.Ticks)
                return MaxRetryDelay;

            delay = TimeSpan.FromTicks(nextTicks);
        }

        return delay;
    }

    private  bool IsRetryWindowExpired(OrderSagaState saga)
    {
        return saga.FirstUnavailableAt.HasValue &&
               DateTime.UtcNow - saga.FirstUnavailableAt.Value >= MaxRetryWindow;
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

        OrderNotification = saga.OrderNotification == null? null: new OrderNotificationDto
    {
        OrderId = saga.OrderId,

        NotifyToEmail = saga.OrderNotification.NotifyToEmail,
        NotifyToSMS = saga.OrderNotification.NotifyToSMS,
        NotifyToPaci = saga.OrderNotification.NotifyToPaci,

        EmailSendStatus = saga.OrderNotification.EmailSendStatus,
        SMSSendStatus = saga.OrderNotification.SMSSendStatus,
        PaciSendStatus = saga.OrderNotification.PaciSendStatus
    }
    };
}
