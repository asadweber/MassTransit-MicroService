using Domain;
using Domain.Repositories;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;


namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"))
                   .UseApplicationServiceProvider(provider));


        // MongoDB
        var mongoSettings = configuration.GetSection("MongoDb").Get<MongoDbSettings>()!;
        services.AddSingleton(mongoSettings);
        services.AddSingleton<IMongoClient>(_ =>
            new MongoClient(mongoSettings.ConnectionString));
        services.AddSingleton<IMongoDatabase>(provider =>
            provider.GetRequiredService<IMongoClient>()
                    .GetDatabase(mongoSettings.DatabaseName));


        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();

        return services;
    }
}
