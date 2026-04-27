namespace WarehouseAutomatisaion.Desktop.Data;

public static class DesktopClientStartupService
{
    public static DesktopClientStartupResult Validate(string actorName)
    {
        var config = DesktopRemoteDatabaseSettings.Snapshot();
        if (!config.Enabled)
        {
            return DesktopClientStartupResult.LocalMode();
        }

        if (string.IsNullOrWhiteSpace(config.Host)
            || string.IsNullOrWhiteSpace(config.Database)
            || string.IsNullOrWhiteSpace(config.User))
        {
            return DesktopClientStartupResult.Failure(
                "Включен режим серверной БД, но настройки подключения заполнены не полностью. Проверьте Host, Database и User в appsettings.local.json.");
        }

        try
        {
            var backplane = DesktopMySqlBackplaneService.CreateDefault();
            backplane.EnsureReady(actorName);

            var operational = OperationalMySqlDesktopService.TryCreateConfigured()
                ?? throw new InvalidOperationException("Не удалось создать подключение к операционной MySQL-схеме.");

            operational.EnsureOperationalSchemaAccessible();

            return DesktopClientStartupResult.SharedMode(config.Host, config.Port, config.Database, config.User);
        }
        catch (Exception exception)
        {
            return DesktopClientStartupResult.Failure(
                "Не удалось подключить клиент к серверной БД. Локальный fallback в общем режиме отключен. " +
                $"Проверьте доступ к MySQL, права пользователя и наличие операционной схемы.\n\n{exception.Message}");
        }
    }
}

public sealed record DesktopClientStartupResult(
    bool CanStart,
    bool UsesSharedDatabase,
    string Message,
    string? Host = null,
    int? Port = null,
    string? Database = null,
    string? User = null)
{
    public static DesktopClientStartupResult LocalMode()
    {
        return new DesktopClientStartupResult(
            CanStart: true,
            UsesSharedDatabase: false,
            Message: "Клиент запущен в локальном режиме.");
    }

    public static DesktopClientStartupResult SharedMode(string host, int port, string database, string user)
    {
        return new DesktopClientStartupResult(
            CanStart: true,
            UsesSharedDatabase: true,
            Message: $"Клиент подключен к серверной БД {host}:{port}/{database} под пользователем {user}.",
            Host: host,
            Port: port,
            Database: database,
            User: user);
    }

    public static DesktopClientStartupResult Failure(string message)
    {
        return new DesktopClientStartupResult(
            CanStart: false,
            UsesSharedDatabase: true,
            Message: message);
    }
}
