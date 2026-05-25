using Microsoft.Extensions.DependencyInjection;
using WarehouseAutomatisaion.Application.Services;

namespace WarehouseAutomatisaion.Application;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IWarehouseOverviewService, WarehouseOverviewService>();
        services.AddScoped<IWarehouseTaskService, WarehouseTaskService>();
        services.AddScoped<IOneCExchangeService, OneCExchangeService>();

        // Sprint 5: AI распознавание накладных. Matcher — чистая функция,
        // безопасно как singleton. Orchestrator зависит от vision + catalog reader +
        // matcher и регистрируется как transient (для async safety).
        services.AddSingleton<InvoiceLineMatcher>();
        services.AddTransient<InvoiceRecognitionService>();

        return services;
    }
}
