using Microsoft.Extensions.Options;
using MySqlConnector;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// Единая точка построения MySqlConnection для Infrastructure-DAO.
// До этого момента построение жило в Desktop/Data/DesktopMySqlCommandRunner
// и Tsd/Services/Tsd*Service — теперь общая обёртка, чтобы CLAUDE.md
// «прямой SQL только в Infrastructure/Persistence/» выполнялось буквально.
//
// Каждый DAO получает MySqlExecutor через DI, вызывает OpenConnectionAsync()
// и сам владеет MySqlConnection (с using await). Это позволяет коду делать
// многошаговые операции внутри одной TX (см. MySqlShipmentPickingService).
public sealed class MySqlExecutor
{
    private const int ConnectTimeoutSeconds = 4;
    private const int CommandTimeoutSeconds = 12;

    private readonly IOptionsMonitor<MySqlPersistenceOptions> _options;

    public MySqlExecutor(IOptionsMonitor<MySqlPersistenceOptions> options)
    {
        _options = options;
    }

    public MySqlPersistenceOptions CurrentOptions => _options.CurrentValue;

    public async Task<MySqlConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var current = _options.CurrentValue;
        if (!current.HasCompleteConnection)
        {
            throw new InvalidOperationException(
                "RemoteDatabase options не настроены — Infrastructure MySQL DAO недоступны.");
        }

        var connection = new MySqlConnection(BuildConnectionString(current));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string BuildConnectionString(MySqlPersistenceOptions options)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)Math.Max(1, options.Port),
            Database = options.Database,
            UserID = options.User,
            Password = options.Password,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = ConnectTimeoutSeconds,
            DefaultCommandTimeout = CommandTimeoutSeconds,
            SslMode = MySqlSslMode.Preferred,
            Pooling = true,
            MinimumPoolSize = 0,
            MaximumPoolSize = 8,
            ConnectionIdleTimeout = 180,
            UseCompression = true
        };

        return builder.ConnectionString;
    }

    public static int DefaultCommandTimeoutSeconds => CommandTimeoutSeconds;
}
