using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Infrastructure.Options;
using WarehouseAutomatisaion.Infrastructure.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed record DesktopScanLookupServices(
    IProductBarcodeLookup ProductLookup,
    IStorageCellLookup CellLookup,
    IScanOperationLogger OperationLogger,
    IWarehouseOperationLogReader OperationLogReader,
    IStockLocationBootstrapper StockLocationBootstrapper);

public static class DesktopScanLookupFactory
{
    public static DesktopScanLookupServices? TryCreate()
    {
        var configured = DesktopRemoteDatabaseSettings.TryBuildOptions();
        if (configured is null)
        {
            return null;
        }

        var options = new MySqlPersistenceOptions
        {
            Enabled = true,
            Host = configured.Host,
            Port = configured.Port,
            Database = configured.DatabaseName,
            User = configured.User,
            Password = configured.Password
        };

        var executor = new MySqlExecutor(new StaticOptionsMonitor<MySqlPersistenceOptions>(options));
        return new DesktopScanLookupServices(
            new MySqlProductBarcodeLookup(executor),
            new MySqlStorageCellLookup(executor),
            new MySqlScanOperationLogger(executor),
            new MySqlWarehouseOperationLogReader(executor),
            new MySqlStockLocationBootstrapper(executor));
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public StaticOptionsMonitor(T value)
        {
            CurrentValue = value;
        }

        public T CurrentValue { get; }

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
