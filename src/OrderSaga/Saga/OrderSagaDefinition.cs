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
                r.Intervals(
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(15),
                    TimeSpan.FromSeconds(30)
                ));

            endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
        }
    }
}
