using System.Text.Json;

namespace WarehouseAutomatisaion.Desktop.Data;

internal static class DesktopRemoteDatabaseSettings
{
    private static readonly object Sync = new();
    private static DesktopRemoteDatabaseConfig? _cached;

    public static OperationalMySqlDesktopOptions? TryBuildOptions()
    {
        var config = Load();
        if (!config.Enabled)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(config.Host)
            || string.IsNullOrWhiteSpace(config.Database)
            || string.IsNullOrWhiteSpace(config.User))
        {
            return null;
        }

        return new OperationalMySqlDesktopOptions
        {
            Host = config.Host,
            Port = config.Port,
            DatabaseName = config.Database,
            User = config.User,
            Password = config.Password,
            MysqlExecutablePath = config.MysqlExecutablePath
        };
    }

    internal static bool IsRemoteDatabaseEnabled()
    {
        return Load().Enabled;
    }

    internal static DesktopRemoteDatabaseConfig Snapshot()
    {
        return Load().Clone();
    }

    private static DesktopRemoteDatabaseConfig Load()
    {
        lock (Sync)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            var config = new DesktopRemoteDatabaseConfig();
            MergeFromFile(config, Path.Combine(AppContext.BaseDirectory, "appsettings.json"), allowDisableRemote: true);
            MergeFromFile(config, Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"), allowDisableRemote: false);
            MergeFromEnvironment(config);

            _cached = config;
            return config;
        }
    }

    private static void MergeFromFile(DesktopRemoteDatabaseConfig target, string path, bool allowDisableRemote)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty("RemoteDatabase", out var section))
        {
            return;
        }

        if (TryGetBoolean(section, "Enabled", out var enabled))
        {
            if (!enabled && !allowDisableRemote && target.Enabled)
            {
                return;
            }

            target.Enabled = enabled;
        }

        target.Host = GetString(section, "Host") ?? target.Host;
        target.Database = GetString(section, "Database") ?? target.Database;
        target.User = GetString(section, "User") ?? target.User;
        target.Password = GetString(section, "Password") ?? target.Password;
        target.MysqlExecutablePath = GetString(section, "MysqlExecutablePath") ?? target.MysqlExecutablePath;

        if (TryGetInt32(section, "Port", out var port))
        {
            target.Port = port;
        }
    }

    private static void MergeFromEnvironment(DesktopRemoteDatabaseConfig target)
    {
        var enabledRaw = Environment.GetEnvironmentVariable("WAREHOUSE_REMOTE_DB_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
        {
            target.Enabled = enabled;
        }

        var host = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_HOST");
        var database = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_DATABASE");
        var user = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_USER");
        var password = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PASSWORD");
        var mysqlExecutablePath = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PATH");
        var portRaw = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PORT");

        var hasExplicitConnectionValue =
            !string.IsNullOrWhiteSpace(host)
            || !string.IsNullOrWhiteSpace(database)
            || !string.IsNullOrWhiteSpace(user)
            || !string.IsNullOrWhiteSpace(password)
            || !string.IsNullOrWhiteSpace(portRaw);

        if (hasExplicitConnectionValue)
        {
            target.Enabled = true;
        }

        target.Host = host ?? target.Host;
        target.Database = database ?? target.Database;
        target.User = user ?? target.User;
        target.Password = password ?? target.Password;
        target.MysqlExecutablePath = mysqlExecutablePath ?? target.MysqlExecutablePath;

        if (int.TryParse(portRaw, out var port))
        {
            target.Port = port;
        }
    }

    private static string? GetString(JsonElement section, string propertyName)
    {
        return section.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetBoolean(JsonElement section, string propertyName, out bool value)
    {
        value = false;
        if (!section.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }

    private static bool TryGetInt32(JsonElement section, string propertyName, out int value)
    {
        value = default;
        if (!section.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && int.TryParse(property.GetString(), out value))
        {
            return true;
        }

        return false;
    }
}

internal sealed class DesktopRemoteDatabaseConfig
{
    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string MysqlExecutablePath { get; set; } = string.Empty;

    public DesktopRemoteDatabaseConfig Clone()
    {
        return new DesktopRemoteDatabaseConfig
        {
            Enabled = Enabled,
            Host = Host,
            Port = Port,
            Database = Database,
            User = User,
            Password = Password,
            MysqlExecutablePath = MysqlExecutablePath
        };
    }
}
