using Application.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Messaging.Events
{
    public record NotificationCompleted
    {
        public Guid CorrelationId { get; init; }

        public OrderDto Order { get; set; } = new();
        public NotificationProcess Process { get; init; }
    }
}
