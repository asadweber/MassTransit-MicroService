using Application.Messaging.Command;
using Application.Messaging.Events;
using MassTransit;

namespace OrderSaga.Saga;

public class OrderStateMachine : MassTransitStateMachine<OrderSagaState>
{
    public State CheckingInventory { get; private set; } = null!;
    public State ProcessingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<OrderCreated> OrderCreated { get; private set; } = null!;
    public Event<InventoryChecked> InventoryChecked { get; private set; } = null!;
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;

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
                .PublishAsync(ctx => ctx.Init<ProcessPayment>(new ProcessPayment
                {
                    CorrelationId = ctx.Saga.CorrelationId,
                    OrderId = ctx.Saga.OrderId,
                }))
                .TransitionTo(ProcessingPayment),

            When(InventoryChecked, x => !x.Message.IsAvailable)
                .TransitionTo(Failed));

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
