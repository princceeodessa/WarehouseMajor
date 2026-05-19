using System.Globalization;
using MySqlConnector;

namespace WarehouseAutomatisaion.Desktop.Data;

internal static class DesktopMySqlCommandRunner
{
    public static string ExecuteScalar(
        OperationalMySqlDesktopOptions options,
        string sql,
        bool useDatabase,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
    {
        using var connection = CreateConnection(options, useDatabase, connectTimeoutSeconds, commandTimeoutSeconds);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = NormalizeSql(sql);
        command.CommandTimeout = commandTimeoutSeconds;

        var result = command.ExecuteScalar();
        return result switch
        {
            null => string.Empty,
            DBNull => string.Empty,
            _ => Convert.ToString(result, CultureInfo.InvariantCulture) ?? string.Empty
        };
    }

    public static int ExecuteNonQuery(
        OperationalMySqlDesktopOptions options,
        string sql,
        bool useDatabase,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
    {
        using var connection = CreateConnection(options, useDatabase, connectTimeoutSeconds, commandTimeoutSeconds);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = NormalizeSql(sql);
        command.CommandTimeout = commandTimeoutSeconds;
        return command.ExecuteNonQuery();
    }

    public static MySqlConnection CreateOpenConnection(
        OperationalMySqlDesktopOptions options,
        bool useDatabase,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
    {
        var connection = CreateConnection(options, useDatabase, connectTimeoutSeconds, commandTimeoutSeconds);
        connection.Open();
        return connection;
    }

    private static MySqlConnection CreateConnection(
        OperationalMySqlDesktopOptions options,
        bool useDatabase,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
    {
        var builder = new MySqlConnectionStringBuilder
        {
            Server = options.Host,
            Port = (uint)Math.Max(1, options.Port),
            UserID = options.User,
            Password = options.Password,
            Database = useDatabase ? options.DatabaseName : string.Empty,
            CharacterSet = "utf8mb4",
            ConnectionTimeout = (uint)Math.Max(1, connectTimeoutSeconds),
            DefaultCommandTimeout = (uint)Math.Max(1, commandTimeoutSeconds),
            SslMode = MySqlSslMode.Preferred,
            AllowUserVariables = true,
            // Release 1.0.137: пулинг физических TCP+TLS+auth — самое дорогое
            // при холодном открытии вкладки (5 SELECT'ов в LoadCatalogSnapshot,
            // 2 в Purchasing, 2 в Warehouse). С пустым пулом каждый платит
            // ~100-300мс на handshake. MinimumPoolSize=2 держит 2 соединения
            // прогретыми после первого использования; их хватает для serial
            // SELECT'ов внутри одного snapshot-load. MaxPoolSize ограничен,
            // чтобы не уйти за лимит max_connections на VPS-MySQL.
            Pooling = true,
            MinimumPoolSize = 2,
            MaximumPoolSize = 16,
            ConnectionIdleTimeout = 300,
            // Release 1.0.137: VPS-MySQL → клиент через интернет. Сжатие
            // на проводе экономит 30-50% времени передачи при загрузке
            // больших snapshot'ов (Catalog ~6MB, Purchasing ~3-5MB).
            // Поддерживается всеми MySQL 5.5+ и MariaDB.
            UseCompression = true
        };

        return new MySqlConnection(builder.ConnectionString);
    }

    private static string NormalizeSql(string sql)
    {
        var normalized = sql.Trim();
        return normalized.Contains("\\n", StringComparison.Ordinal)
            ? normalized.Replace("\\\\r\\\\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace("\\\\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace("\\r\\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace("\\n", Environment.NewLine, StringComparison.Ordinal)
                .Replace("\\\\t", "    ", StringComparison.Ordinal)
                .Replace("\\t", "    ", StringComparison.Ordinal)
            : normalized;
    }
}
