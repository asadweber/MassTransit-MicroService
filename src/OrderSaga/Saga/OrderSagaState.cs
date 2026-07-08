using Application.Dtos;
using MassTransit;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OrderSaga.Saga;

public class OrderSagaState :SagaStateMachineInstance,ISagaVersion
{
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = string.Empty;

    public int Version { get; set; }

    //public int OrderId { get; set; }
    public OrderDto Order { get; set; }

    public DateTime? FirstUnavailableAt { get; set; }

    public int InventoryRetryCount { get; set; }

    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid? InventoryRetryTokenId { get; set; }
}
