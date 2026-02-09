using MyApp.WorkerService;
using MyApp.Infrastructure.DependencyInjection;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configuration de Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Logging.AddSerilog();

_ = builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddHostedService<OpcDataAcquisitionWorker>();

var host = builder.Build();

try
{
    Log.Information("Démarrage de l'hôte...");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "L'hôte s'est arrêté de manière inattendue.");
}
finally
{
    await Log.CloseAndFlushAsync();
}
