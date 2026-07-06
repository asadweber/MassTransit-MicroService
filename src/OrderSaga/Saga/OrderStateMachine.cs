using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;

namespace OrderSaga.Saga;

public class OrderStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public static readonly TimeSpan[] InventoryRetryBackoff =
    [
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30),
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(10),
        TimeSpan.FromMinutes(30),

    ];
    public static readonly TimeSpan InventoryRetryWindow = TimeSpan.FromDays(10);

    static TimeSpan GetInventoryRetryDelay(int retryCount)
    {
        var index = Math.Min(retryCount, InventoryRetryBackoff.Length - 1);
        return InventoryRetryBackoff[index];
    }

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

        Event(() => OrderCreated, x =>
            x.CorrelateBy((state, ctx) => state.OrderId == ctx.Message.Order.Id)
             .SelectId(_ => NewId.NextGuid()));

        Event(() => InventoryChecked, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Event(() => PaymentProcessed, x =>
            x.CorrelateById(ctx => ctx.Message.CorrelationId));

        Schedule(() => InventoryRetry, x => x.InventoryRetryTokenId, x =>
        {
            x.Delay = InventoryRetryBackoff[0];
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
                .PublishAsync(ctx => ctx.Init<ProcessPayment>(new ProcessPayment
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                }))
                .TransitionTo(ProcessingPayment),

            When(InventoryChecked, x => !x.Message.IsAvailable
                                        && (DateTime.UtcNow - (x.Saga.FirstUnavailableAt ?? DateTime.UtcNow)) < InventoryRetryWindow)
                .Schedule(InventoryRetry,
                    ctx => ctx.Init<CheckInventory>(new CheckInventory
                    {
                        CorrelationId = ctx.Saga.CorrelationId,
                        OrderId = ctx.Saga.OrderId,
                    }),
                    ctx => GetInventoryRetryDelay(ctx.Saga.InventoryRetryCount))
                .Then(ctx =>
                {
                    ctx.Saga.FirstUnavailableAt ??= DateTime.UtcNow;
                    ctx.Saga.InventoryRetryCount++;
                }),

            When(InventoryChecked, x => !x.Message.IsAvailable
                                        && (DateTime.UtcNow - (x.Saga.FirstUnavailableAt ?? DateTime.UtcNow)) >= InventoryRetryWindow)
                .TransitionTo(Failed),

            When(InventoryRetry.Received)
                .PublishAsync(ctx => ctx.Init<CheckInventory>(new CheckInventory
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                })));

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
}
