using MassTransit;

namespace OrderSaga.Saga;

public class OrderSagaState :
    SagaStateMachineInstance,
    ISagaVersion
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    public int OrderId { get; set; }
}
