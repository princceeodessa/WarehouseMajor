using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Integrations;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Infrastructure.Ai;
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

        // Sprint 5: AI-провайдеры (OpenAI / Anthropic) для vision и других фич.
        services.AddOptions<AiProvidersOptions>()
            .Bind(configuration.GetSection(AiProvidersOptions.SectionName));

        // Sprint 5 Task 10: IInvoiceVisionService — переключается по AiProviders:Default.
        // Сейчас: только OpenAI. Когда добавится Claude — расширим switch.
        services.AddTransient<IInvoiceVisionService>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<AiProvidersOptions>>();
            var providerName = options.CurrentValue.Default;

            return providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
                ? ActivatorUtilities.CreateInstance<OpenAiInvoiceVisionService>(serviceProvider)
                : throw new NotSupportedException(
                    $"AI provider '{providerName}' not yet implemented. Set AiProviders:Default to 'OpenAI'.");
        });

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
