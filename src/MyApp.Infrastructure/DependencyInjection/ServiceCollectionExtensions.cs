using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using MyApp.Application.Interfaces;
using MyApp.Application.UseCases;
using MyApp.Infrastructure.DapperRepositories;
using MyApp.Infrastructure.OpcUa;
using MyApp.Infrastructure.Kafka;
using MyApp.Infrastructure.StoreForward;

namespace MyApp.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Store & Forward (doit être enregistré avant SqlBatchRepository)
        services.AddSingleton<IStoreForwardService, JsonStoreForwardService>();
        
        // Repositories (Dapper)
        services.AddSingleton<IMachineRepository, MachineRepository>();
        services.AddSingleton<IDataPersistenceService, SqlBatchRepository>();

        // Choix du service OPC UA (Réel vs Simulateur)
        var useSimulator = configuration.GetValue<bool>("OpcSettings:UseSimulator");
        if (useSimulator)
        {
            services.AddSingleton<IFacDataService, MockOpcUaService>();
        }
        else
        {
            services.AddSingleton<IFacDataService, OpcUaSubscriptionService>();
        }

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
