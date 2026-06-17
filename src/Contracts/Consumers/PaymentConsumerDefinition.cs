using Db.Repository;
using MassTransit;

namespace Contracts.Consumers;

public class PaymentConsumerDefinition : ConsumerDefinition<PaymentConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<PaymentConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(r =>
            r.Exponential(3, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));
        // ✅ Transactional Outbox (SQL Server via EF Core)
        endpointConfigurator.UseEntityFrameworkOutbox<AppDbContext>(context);
    }
}
