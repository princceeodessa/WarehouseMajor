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

        return services;
    }
}
