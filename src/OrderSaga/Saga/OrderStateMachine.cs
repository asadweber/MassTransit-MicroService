using Application.Dtos;
using Application.Messaging.Command;
using Application.Messaging.Events;
using AutoMapper;
using Infrastructure.Persistence;
using MassTransit;

namespace OrderSaga.Saga;

/// <summary>
/// Coordinates the complete order-processing workflow:
///
/// OrderCreated
///     ↓
/// CheckingInventory
///     ↓
/// ProcessingPayment
///     ↓
/// Confirmed
///     ↓
/// Finalize
///
/// Failed business processes remain in the saga table for operational
/// visibility and possible recovery. Successfully completed sagas are
/// finalized and removed from the saga repository.
/// </summary>
public class OrderStateMachine : MassTransitStateMachine<OrderSagaState>
{
    #region Retry Configuration

    /// <summary>
    /// Maximum period during which inventory can remain unavailable.
    /// </summary>
    public static readonly TimeSpan MaxRetryWindow =
        TimeSpan.FromDays(7);

    /// <summary>
    /// Delay before the first inventory retry.
    /// </summary>
    public static readonly TimeSpan FirstRetryDelay =
        TimeSpan.FromMinutes(1);

    /// <summary>
    /// Maximum delay between individual inventory retries.
    /// </summary>
    public static readonly TimeSpan MaxRetryDelay =
        TimeSpan.FromDays(1);

    /// <summary>
    /// Exponential backoff multiplier:
    ///
    /// 1m → 5m → 25m → 125m → ...
    /// </summary>
    private const int BackoffFactor = 5;

    #endregion

    #region States

    /// <summary>
    /// Waiting for InventoryChecked after requesting inventory validation.
    /// </summary>
    public State CheckingInventory { get; private set; } = null!;

    /// <summary>
    /// Waiting for PaymentProcessed after requesting payment processing.
    /// </summary>
    public State ProcessingPayment { get; private set; } = null!;

    /// <summary>
    /// Payment succeeded. Waiting for notification fan-out completion.
    /// </summary>
    public State PaymentConfirmed { get; private set; } = null!;

    /// <summary>
    /// Business process failed.
    /// Failed saga instances remain persisted for operational recovery.
    /// </summary>
    public State Failed { get; private set; } = null!;

    #endregion

    #region Events

    /// <summary>
    /// Starts a new order saga.
    /// </summary>
    public Event<OrderCreated> OrderCreated { get; private set; } = null!;

    /// <summary>
    /// Inventory service response.
    /// </summary>
    public Event<InventoryChecked> InventoryChecked { get; private set; } = null!;

    /// <summary>
    /// Payment service response.
    /// </summary>
    public Event<PaymentProcessed> PaymentProcessed { get; private set; } = null!;

    /// <summary>
    /// Notification process completion event.
    /// </summary>
    public Event<NotificationCompleted> NotificationCompleted
    {
        get;
        private set;
    } = null!;

    #endregion

    #region Schedules

    /// <summary>
    /// Scheduled inventory retry.
    /// </summary>
    public Schedule<OrderSagaState, CheckInventory> InventoryRetry
    {
        get;
        private set;
    } = null!;

    #endregion

    private readonly ILogger<OrderStateMachine> _logger;
    private readonly IMapper _mapper;

    public OrderStateMachine(
        ILogger<OrderStateMachine> logger,
        IMapper mapper)
    {
        _logger = logger;
        _mapper = mapper;

        InstanceState(x => x.CurrentState);

        ConfigureEvents();
        ConfigureSchedules();
        ConfigureInitialState();
        ConfigureInventoryState();
        ConfigurePaymentState();
        ConfigurePaymentConfirmedState();
        ConfigureFailedState();

        // Finalized saga instances are removed from the repository.
        SetCompletedWhenFinalized();
    }

    #region Event Configuration

    private void ConfigureEvents()
    {
        // OrderCreated is the first event.
        //
        // There is no saga CorrelationId yet, therefore correlate using
        // the business OrderId and generate a new CorrelationId.
        Event(() => OrderCreated, x =>
        {
            x.CorrelateBy(
                (instance, context) =>
                    instance.OrderId == context.Message.Order.Id);

            x.SelectId(_ => NewId.NextGuid());
        });

        Event(() => InventoryChecked, x =>
        {
            x.CorrelateById(
                context => context.Message.CorrelationId);
        });

        Event(() => PaymentProcessed, x =>
        {
            x.CorrelateById(
                context => context.Message.CorrelationId);
        });

        Event(() => NotificationCompleted, x =>
        {
            x.CorrelateById(
                context => context.Message.CorrelationId);
        });
    }

    #endregion

    #region Schedule Configuration

    private void ConfigureSchedules()
    {
        Schedule(
            () => InventoryRetry,
            x => x.InventoryRetryTokenId,
            x =>
            {
                x.Delay = FirstRetryDelay;

                x.Received = r =>
                    r.CorrelateById(
                        context => context.Message.CorrelationId);
            });
    }

    #endregion

    #region Initial State

    private void ConfigureInitialState()
    {
        Initially(
            When(OrderCreated)
                .Then(ctx =>
                {
                    var order = ctx.Message.Order;
                    var notification = order.OrderNotification;

                    var correlationId =
                        ctx.Saga.CorrelationId;

                    ctx.Saga.OrderId = order.Id;
                    ctx.Saga.CustomerName = order.CustomerName;
                    ctx.Saga.OrderDate = order.OrderDate;
                    ctx.Saga.TotalAmount = order.TotalAmount;
                    ctx.Saga.Status = order.Status;

                    ctx.Saga.OrderDetails =
                        order.OrderDetails
                            .Select(d => new SagaOrderDetail
                            {
                                OrderDetailId = d.Id,
                                OrderSagaStateCorrelationId = correlationId,
                                ProductId = d.ProductId,
                                OrderQty = d.OrderQty,
                                UnitPrice = d.UnitPrice,
                                Total = d.Total
                            })
                            .ToList();

                    ctx.Saga.OrderNotification =
                        notification == null
                            ? null
                            : new SagaOrderNotification
                            {
                                OrderNotificationId = notification.Id,
                                OrderSagaStateCorrelationId =correlationId,
                                NotifyToEmail =notification.NotifyToEmail,
                                NotifyToSMS =notification.NotifyToSMS,
                                NotifyToPaci =notification.NotifyToPaci,
                                EmailSendStatus =notification.EmailSendStatus,
                                SMSSendStatus =notification.SMSSendStatus,
                                PaciSendStatus =notification.PaciSendStatus,
                                NotificationSendStatus =notification.NotificationSendStatus
                            };

                    using var correlationScope =
                        Serilog.Context.LogContext.PushProperty(
                            "CorrelationId",
                            ctx.Saga.CorrelationId);

                    using var orderScope =
                        Serilog.Context.LogContext.PushProperty(
                            "OrderId",
                            ctx.Saga.OrderId);

                    _logger.LogInformation(
                        "Order {OrderId} created. Starting inventory check",
                        ctx.Saga.OrderId);
                })

                .PublishAsync(ctx =>
                    ctx.Init<CheckInventory>(
                        ToCheckInventory(ctx.Saga)))

                .TransitionTo(CheckingInventory)

                .Then(ctx =>
                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "OrderCreated -> CheckingInventory",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId)));
    }

    #endregion

    #region Checking Inventory

    private void ConfigureInventoryState()
    {
        During(
            CheckingInventory,

            // ---------------------------------------------------------
            // Inventory AVAILABLE
            // ---------------------------------------------------------
            When(
                InventoryChecked,
                x => x.Message.IsAvailable)

                .Unschedule(InventoryRetry)

                .Then(ctx =>
                {
                    ctx.Saga.FirstUnavailableAt = null;
                    ctx.Saga.InventoryRetryCount = 0;
                    ctx.Saga.NextInventoryRetryAt = null;
                    ctx.Saga.Status = "Stock Available";

                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "Inventory available",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId);
                })

                .PublishAsync(ctx =>
                    ctx.Init<ProcessPayment>(
                        ToProcessPayment(ctx.Saga)))

                .TransitionTo(ProcessingPayment)

                .Then(ctx =>
                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "InventoryChecked -> ProcessingPayment",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId)),

            // ---------------------------------------------------------
            // Inventory NOT AVAILABLE
            // ---------------------------------------------------------
            When(
                InventoryChecked,
                x => !x.Message.IsAvailable)

                .Then(ctx =>
                {
                    ctx.Saga.FirstUnavailableAt ??= DateTime.UtcNow;

                    ctx.Saga.Status =
                        "Stock Not Available";
                })

                .IfElse(
                    ctx => IsRetryWindowExpired(ctx.Saga),

                    // -------------------------------------------------
                    // RETRY WINDOW EXPIRED
                    // -------------------------------------------------
                    expired => expired

                        .Unschedule(InventoryRetry)

                        .Then(ctx =>
                        {
                            ctx.Saga.Status =
                                "Inventory Retry Expired";

                            _logger.LogWarning(
                                "Order {OrderId} [{CorrelationId}]: " +
                                "Inventory unavailable for {RetryWindow}. " +
                                "Moving to Failed",
                                ctx.Saga.OrderId,
                                ctx.Saga.CorrelationId,
                                MaxRetryWindow);
                        })

                        .TransitionTo(Failed),

                    // -------------------------------------------------
                    // SCHEDULE NEXT RETRY
                    // -------------------------------------------------
                    retry => retry

                        .Then(ctx =>
                        {
                            ctx.Saga.InventoryRetryCount++;

                            var delay =
                                GetRetryDelay(
                                    ctx.Saga.InventoryRetryCount);

                            ctx.Saga.NextInventoryRetryAt = DateTime.UtcNow + delay;

                            _logger.LogInformation(
                                "Order {OrderId} [{CorrelationId}]: " +
                                "Inventory unavailable. " +
                                "Retry #{RetryCount} scheduled in {Delay}",
                                ctx.Saga.OrderId,
                                ctx.Saga.CorrelationId,
                                ctx.Saga.InventoryRetryCount,
                                delay);
                        })

                        .Unschedule(InventoryRetry)

                        .Schedule(
                            InventoryRetry,
                            ctx =>
                                ctx.Init<CheckInventory>(
                                    ToCheckInventory(ctx.Saga)),
                            ctx =>
                                ctx.Saga.NextInventoryRetryAt!.Value
                                - DateTimeOffset.UtcNow)),

            // ---------------------------------------------------------
            // SCHEDULED INVENTORY RETRY
            // ---------------------------------------------------------
            When(InventoryRetry.Received)

                .Then(ctx =>
                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "Inventory retry fired",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId))

                .PublishAsync(ctx =>
                    ctx.Init<CheckInventory>(
                        ToCheckInventory(ctx.Saga))),

            // ---------------------------------------------------------
            // Late / duplicate messages
            // ---------------------------------------------------------
            Ignore(OrderCreated),
            Ignore(PaymentProcessed),
            Ignore(NotificationCompleted));
    }

    #endregion

    #region Payment

    private void ConfigurePaymentState()
    {
        During(
            ProcessingPayment,

            // ---------------------------------------------------------
            // PAYMENT SUCCESS
            // ---------------------------------------------------------
            When(
                PaymentProcessed,
                x => x.Message.IsSuccess)

                .Then(ctx =>
                {
                    ctx.Saga.Status =
                        "Payment Complete";

                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "Payment completed",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId);
                })

                .PublishAsync(ctx =>
                    ctx.Init<OrderConfirmed>(
                        ToOrderConfirmed(ctx.Saga)))

                .TransitionTo(PaymentConfirmed)

                // No channels requested (or no notification record at all) —
                // nothing will ever raise NotificationCompleted, so finalize now.
                .If(
                    ctx => IsNotificationFanOutComplete(ctx.Saga),
                    both => both
                        .Then(ctx => ctx.Saga.Status = "Completed")
                        .Finalize()),

            // ---------------------------------------------------------
            // PAYMENT FAILURE
            // ---------------------------------------------------------
            When(
                PaymentProcessed,
                x => !x.Message.IsSuccess)

                .Then(ctx =>
                {
                    ctx.Saga.Status =
                        "Payment Failed";

                    _logger.LogWarning(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "Payment declined. Moving to Failed",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId);
                })

                .TransitionTo(Failed),

            // ---------------------------------------------------------
            // LATE / DUPLICATE EVENTS
            // ---------------------------------------------------------
            Ignore(OrderCreated),
            Ignore(InventoryChecked),
            Ignore(InventoryRetry.Received),
            Ignore(NotificationCompleted));
    }

    #endregion

    #region Confirmed / Notification Fan-out

    private void ConfigurePaymentConfirmedState()
    {
        During(
            PaymentConfirmed,

            // ---------------------------------------------------------
            // Email channel done
            // ---------------------------------------------------------
            When(
                NotificationCompleted,
                x => x.Message.Process == NotificationProcess.Email)

                .Then(ctx =>
                {
                    ctx.Saga.OrderNotification.EmailSendStatus = true;
                    ctx.Saga.Status = "Email Notification Complete";
                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "Email notification completed",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId);
                })

                .If(
                    ctx => IsNotificationFanOutComplete(ctx.Saga),
                    both => both
                        .Then(ctx => ctx.Saga.Status = "Completed")
                        .Finalize()),

            // ---------------------------------------------------------
            // SMS channel done
            // ---------------------------------------------------------
            When(
                NotificationCompleted,
                x => x.Message.Process == NotificationProcess.SMS)

                .Then(ctx =>
                {
                    ctx.Saga.OrderNotification.SMSSendStatus = true;
                    ctx.Saga.Status= "SMS Notification Completed";
                    _logger.LogInformation(
                        "Order {OrderId} [{CorrelationId}]: " +
                        "SMS notification completed",
                        ctx.Saga.OrderId,
                        ctx.Saga.CorrelationId);
                })

                .If(
                    ctx => IsNotificationFanOutComplete(ctx.Saga),
                    both => both
                        .Then(ctx => {
                            ctx.Saga.Status = "Completed"; 
                        })
                        .Finalize()),

            // Late / duplicate messages
            Ignore(OrderCreated),
            Ignore(InventoryChecked),
            Ignore(PaymentProcessed),
            Ignore(InventoryRetry.Received));
    }

    #endregion

    #region Failed

    private void ConfigureFailedState()
    {
        During(
            Failed,

            // Failed saga remains persisted.
            //
            // This allows:
            // - operational investigation
            // - admin dashboard
            // - manual recovery
            // - future retry/reprocessing
            Ignore(InventoryChecked),
            Ignore(PaymentProcessed),
            Ignore(OrderCreated),
            Ignore(NotificationCompleted),
            Ignore(InventoryRetry.Received));
    }

    #endregion

    #region Retry Helpers

    /// <summary>
    /// Calculates exponential inventory retry delay:
    ///
    /// Retry 1 = 1 minute
    /// Retry 2 = 5 minutes
    /// Retry 3 = 25 minutes
    /// Retry 4 = 125 minutes
    ///
    /// The delay is capped at MaxRetryDelay.
    /// </summary>
    private static TimeSpan GetRetryDelay(int retryCount)
    {
        if (retryCount <= 1)
            return FirstRetryDelay;

        var delay = FirstRetryDelay;

        for (var i = 1; i < retryCount; i++)
        {
            if (delay >= MaxRetryDelay)
                return MaxRetryDelay;

            if (delay.Ticks >
                MaxRetryDelay.Ticks / BackoffFactor)
            {
                return MaxRetryDelay;
            }

            delay = TimeSpan.FromTicks(
                delay.Ticks * BackoffFactor);
        }

        return delay > MaxRetryDelay
            ? MaxRetryDelay
            : delay;
    }

    /// <summary>
    /// A channel is "done" if the customer never opted into it, or it opted
    /// in and has since completed. Fan-out is complete when both channels
    /// are done — so an order with only Email (or only SMS) enabled doesn't
    /// wait forever on the channel it never requested.
    /// </summary>
    private static bool IsNotificationFanOutComplete(
        OrderSagaState saga)
    {
        var notification = saga.OrderNotification;

        if (notification is null)
            return true;

        var emailDone =
            !notification.NotifyToEmail || notification.EmailSendStatus;

        var smsDone =
            !notification.NotifyToSMS || notification.SMSSendStatus;

        return emailDone && smsDone;
    }

    private static bool IsRetryWindowExpired(
        OrderSagaState saga)
    {
        return saga.FirstUnavailableAt.HasValue &&
               DateTimeOffset.UtcNow -
               saga.FirstUnavailableAt.Value >=
               MaxRetryWindow;
    }

    #endregion

    #region Message Builders

    private CheckInventory ToCheckInventory(
        OrderSagaState saga)
    {
        return new CheckInventory
        {
            CorrelationId = saga.CorrelationId,
            Order = ToOrderDto(saga)
        };
    }

    private ProcessPayment ToProcessPayment(
        OrderSagaState saga)
    {
        return new ProcessPayment
        {
            CorrelationId = saga.CorrelationId,
            Order = ToOrderDto(saga)
        };
    }

    private OrderConfirmed ToOrderConfirmed(
        OrderSagaState saga)
    {
        return new OrderConfirmed
        {
            CorrelationId = saga.CorrelationId,
            Order = ToOrderDto(saga)
        };
    }

    private OrderDto ToOrderDto(
        OrderSagaState saga)
    {
        return new OrderDto
        {
            Id = saga.OrderId,

            CustomerName =
                saga.CustomerName,

            OrderDate =
                saga.OrderDate,

            TotalAmount =
                saga.TotalAmount,

            Status =
                saga.Status,

            OrderDetails =
                saga.OrderDetails
                    .Select(d => new OrderDetailDto
                    {
                        Id = d.OrderDetailId,
                        OrderId = saga.OrderId,
                        ProductId = d.ProductId,
                        OrderQty = d.OrderQty,
                        UnitPrice = d.UnitPrice,
                        Total = d.Total
                    })
                    .ToList(),

            OrderNotification =
                saga.OrderNotification == null
                    ? null
                    : new OrderNotificationDto
                    {
                        Id =
                            saga.OrderNotification
                                .OrderNotificationId,

                        OrderId =
                            saga.OrderId,

                        NotifyToEmail =
                            saga.OrderNotification
                                .NotifyToEmail,

                        NotifyToSMS =
                            saga.OrderNotification
                                .NotifyToSMS,

                        NotifyToPaci =
                            saga.OrderNotification
                                .NotifyToPaci,

                        EmailSendStatus =
                            saga.OrderNotification
                                .EmailSendStatus,

                        SMSSendStatus =
                            saga.OrderNotification
                                .SMSSendStatus,

                        PaciSendStatus =
                            saga.OrderNotification
                                .PaciSendStatus,

                        NotificationSendStatus =
                            saga.OrderNotification
                                .NotificationSendStatus
                    }
        };
    }

    #endregion
}