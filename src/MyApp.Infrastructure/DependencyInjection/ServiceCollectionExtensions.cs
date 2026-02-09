using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MyApp.Application.Interfaces;
using MyApp.Application.UseCases;
using MyApp.Infrastructure.DapperRepositories;
using MyApp.Infrastructure.OpcUa;
using MyApp.Infrastructure.Kafka;

namespace MyApp.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Repositories (Dapper)
        services.AddSingleton<IMachineRepository, MachineRepository>();
        services.AddSingleton<IDataPersistenceService, SqlBatchRepository>();

        // OPC UA Service
        services.AddSingleton<IFacDataService, OpcUaSubscriptionService>();

        // Kafka (Optional)
        services.AddSingleton<KafkaProducer>();

        return services;
    }

    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Use Cases
        services.AddSingleton<ProcessIncomingData>();

        return services;
    }
}
