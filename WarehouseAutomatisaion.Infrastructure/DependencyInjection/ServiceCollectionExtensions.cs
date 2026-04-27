using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Integrations;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Infrastructure.Integrations;
using WarehouseAutomatisaion.Infrastructure.Options;
using WarehouseAutomatisaion.Infrastructure.Persistence;

namespace WarehouseAutomatisaion.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OneCIntegrationOptions>()
            .Bind(configuration.GetSection(OneCIntegrationOptions.SectionName));

        services.AddSingleton<InMemoryWarehouseDataStore>();
        services.AddSingleton<IProductRepository, InMemoryProductRepository>();
        services.AddSingleton<IStorageCellRepository, InMemoryStorageCellRepository>();
        services.AddSingleton<IInventoryBalanceRepository, InMemoryInventoryBalanceRepository>();
        services.AddSingleton<IWarehouseTaskRepository, InMemoryWarehouseTaskRepository>();
        services.AddSingleton<IIntegrationCheckpointRepository, InMemoryIntegrationCheckpointRepository>();

        services.AddHttpClient<IOneCExchangeGateway, OneCHttpExchangeGateway>((serviceProvider, httpClient) =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<OneCIntegrationOptions>>().CurrentValue;
            if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
            {
                httpClient.BaseAddress = baseUri;
            }

            httpClient.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHostedService<OneCSynchronizationBackgroundService>();

        return services;
    }
}
