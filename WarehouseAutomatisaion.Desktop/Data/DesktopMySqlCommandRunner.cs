using System.Globalization;
using MySqlConnector;

namespace WarehouseAutomatisaion.Desktop.Data;

internal static class DesktopMySqlCommandRunner
{
    // v1.0.53: bump GROUP_CONCAT/JSON_ARRAYAGG buffer per session. The MySQL
    // default group_concat_max_len is 1024 bytes; the operational snapshot
    // returns ~5-10 MB of JSON_ARRAYAGG output (2073 customers + 3790 orders +
    // 3453 shipments + 12978 stock balances) which got silently truncated to
    // 1024 bytes, breaking the C# JSON parser. The whole TryLoadSnapshot then
    // threw and the app fell back to demo fixtures — making it look like the
    // 1C import never took effect. Setting a 1 GiB ceiling per session fixes
    // every JSON_ARRAYAGG query in one place.
    private const long GroupConcatMaxLen = 1073741824L;

    private static void TuneSession(MySqlConnection connection)
    {
        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = $"SET SESSION group_concat_max_len = {GroupConcatMaxLen}";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort; some MySQL flavours (e.g. PlanetScale) reject SET.
        }
    }

    public static string ExecuteScalar(
        OperationalMySqlDesktopOptions options,
        string sql,
        bool useDatabase,
        int connectTimeoutSeconds,
        int commandTimeoutSeconds)
    {
        using var connection = CreateConnection(options, useDatabase, connectTimeoutSeconds, commandTimeoutSeconds);
        connection.Open();
        TuneSession(connection);

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
        TuneSession(connection);

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
        TuneSession(connection);
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
            SslMode = MySqlSslMode.Preferred,
            AllowUserVariables = true,
            MinimumPoolSize = 1,
            MaximumPoolSize = 5
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
