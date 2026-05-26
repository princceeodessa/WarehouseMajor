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

        // Sprint 5: IInvoiceVisionService — переключается по AiProviders:Default.
        // Поддерживаются: OpenAI (gpt-4o) и Anthropic (claude-opus-4-7).
        services.AddTransient<IInvoiceVisionService>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptionsMonitor<AiProvidersOptions>>();
            var providerName = options.CurrentValue.Default;

            return providerName.ToLowerInvariant() switch
            {
                "openai" => ActivatorUtilities.CreateInstance<OpenAiInvoiceVisionService>(serviceProvider),
                "anthropic" => ActivatorUtilities.CreateInstance<ClaudeInvoiceVisionService>(serviceProvider),
                _ => throw new NotSupportedException(
                    $"AI provider '{providerName}' not supported. Use 'OpenAI' or 'Anthropic'.")
            };
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
