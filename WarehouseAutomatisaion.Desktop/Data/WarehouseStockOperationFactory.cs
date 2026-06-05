using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Infrastructure.Options;
using WarehouseAutomatisaion.Infrastructure.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class WarehouseStockOperationFactory
{
    public static IWarehouseStockOperationService? TryCreate()
    {
        var config = DesktopRemoteDatabaseSettings.Snapshot();
        if (!config.Enabled
            || string.IsNullOrWhiteSpace(config.Host)
            || string.IsNullOrWhiteSpace(config.Database)
            || string.IsNullOrWhiteSpace(config.User))
        {
            return null;
        }

        var options = new MySqlPersistenceOptions
        {
            Enabled = true,
            Host = config.Host,
            Port = config.Port,
            Database = config.Database,
            User = config.User,
            Password = config.Password
        };

        return new MySqlWarehouseStockOperationService(new MySqlExecutor(options));
    }
}
