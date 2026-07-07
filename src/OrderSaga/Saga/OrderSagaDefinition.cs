using Infrastructure.Persistence;
using MassTransit;
using System;
using System.Collections.Generic;
using System.Text;

namespace OrderSaga.Saga
{
    public class OrderSagaDefinition : SagaDefinition<OrderSagaState>
    {
        protected override void ConfigureSaga(
            IReceiveEndpointConfigurator endpointConfigurator,
            ISagaConfigurator<OrderSagaState> sagaConfigurator,
            IRegistrationContext context)
        {
            endpointConfigurator.UseMessageRetry(r =>
            {
                r.Exponential(
                    retryLimit: 5,
                    minInterval: TimeSpan.FromSeconds(1),
                    maxInterval: TimeSpan.FromMinutes(1),
                    intervalDelta: TimeSpan.FromSeconds(5));
            });

            endpointConfigurator.UseDelayedRedelivery(r =>
            {
                r.Intervals(
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(10),
                    TimeSpan.FromMinutes(30),
                    TimeSpan.FromHours(1),
                    TimeSpan.FromHours(6),
                    TimeSpan.FromHours(12),
                    TimeSpan.FromDays(1),
                    TimeSpan.FromDays(3),
                    TimeSpan.FromDays(7));
            });

        }
    }
}
