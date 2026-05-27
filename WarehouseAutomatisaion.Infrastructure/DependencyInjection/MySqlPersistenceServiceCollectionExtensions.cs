using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Infrastructure.Options;
using WarehouseAutomatisaion.Infrastructure.Persistence;

namespace WarehouseAutomatisaion.Infrastructure.DependencyInjection;

// Регистрирует MySQL-based DAO для точечного доступа к app_* таблицам.
// Биндит секцию "RemoteDatabase" из IConfiguration → MySqlPersistenceOptions.
//
// Вызывается из Tsd/Program.cs (а в будущем из Desktop, если решит мигрировать
// с module-snapshots на точечные DAO).
public static class MySqlPersistenceServiceCollectionExtensions
{
    public static IServiceCollection AddMySqlPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<MySqlPersistenceOptions>()
            .Bind(configuration.GetSection(MySqlPersistenceOptions.SectionName));

        services.AddSingleton<MySqlExecutor>();
        services.AddScoped<IProductBarcodeLookup, MySqlProductBarcodeLookup>();
        services.AddScoped<IStorageCellLookup, MySqlStorageCellLookup>();
        services.AddScoped<IScanOperationLogger, MySqlScanOperationLogger>();
        services.AddScoped<IShipmentPickingService, MySqlShipmentPickingService>();
        services.AddScoped<IWarehouseOperationLogReader, MySqlWarehouseOperationLogReader>();
        services.AddScoped<IStockLocationBootstrapper, MySqlStockLocationBootstrapper>();

        return services;
    }
}
