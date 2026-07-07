using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;

namespace OrderSaga.Saga;

public class OrderStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public static readonly TimeSpan MaxRetryWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);
    private const int BackoffFactor = 5;

    public State CheckingInventory { get; private set; } = null!;
    public State ProcessingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<OrderCreated> OrderCreated { get; private set; } = null!;
    public Event<InventoryChecked> InventoryChecked { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;

    public Schedule<OrderSagaState, CheckInventory> InventoryRetry { get; private set; } = null!;

    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        // First event for a saga instance: correlate by OrderId (no CorrelationId exists yet)
        // and mint a new one. All later events correlate by that generated CorrelationId.
        Event(() => OrderCreated, x =>
            x.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.Order.Id)
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

        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    ctx.Saga.OrderId = ctx.Message.Order.Id;

                })
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                }))
                .TransitionTo(CheckingInventory));

        During(CheckingInventory,
            When(InventoryChecked, x => x.Message.IsAvailable)
                .Unschedule(InventoryRetry)
                .Then(ctx =>
                {
                    ctx.Saga.FirstUnavailableAt = null;
                    ctx.Saga.InventoryRetryCount = 0;
                })
                .PublishAsync(ctx => ctx.Init<ProcessPayment>(new ProcessPayment
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                }))
                .TransitionTo(ProcessingPayment),

            // Still unavailable: give up only once MaxRetryWindow (7d from first-seen-unavailable)
            // has elapsed; otherwise schedule another check with growing backoff.
            When(InventoryChecked, x => !x.Message.IsAvailable)
                .IfElse(ctx => IsRetryWindowExpired(ctx.Saga),
                    stillUnavailable => stillUnavailable
                        .TransitionTo(Failed),
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
                                OrderId = ctx.Saga.OrderId,
                            }),
                            ctx => GetRetryDelay(ctx.Saga.InventoryRetryCount))),

            // Fires when the scheduled delay elapses (token stored via InventoryRetryTokenId) —
            // re-publish CheckInventory to poll availability again.
            When(InventoryRetry.Received)
                .PublishAsync(ctx => ctx.Init<CheckInventory>(ctx.Message)));

        During(ProcessingPayment,
            When(PaymentProcessed, x => x.Message.IsSuccess)
                .PublishAsync(ctx => ctx.Init<OrderConfirmed>(new OrderConfirmed
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                }))
                .TransitionTo(Confirmed)
                .Finalize(),

            When(PaymentProcessed, x => !x.Message.IsSuccess)
                .TransitionTo(Failed));

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
