namespace WarehouseAutomatisaion.Desktop.Data;

public static class DesktopClientStartupService
{
    public static DesktopClientStartupResult Validate(string actorName)
    {
        var config = DesktopRemoteDatabaseSettings.Snapshot();
        if (!config.Enabled)
        {
            return DesktopClientStartupResult.LocalMode(actorName);
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
            var profile = backplane.EnsureUserProfile(actorName);

            var operational = OperationalMySqlDesktopService.TryCreateConfigured()
                ?? throw new InvalidOperationException("Не удалось создать подключение к операционной MySQL-схеме.");

            operational.EnsureOperationalSchemaAccessible();

            return DesktopClientStartupResult.SharedMode(config.Host, config.Port, config.Database, config.User, profile);
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
    string? User = null,
    string UserName = "",
    string DisplayName = "",
    string PrimaryRoleCode = "manager",
    string PrimaryRoleDisplayName = "Менеджер",
    IReadOnlyList<string>? RoleCodes = null)
{
    public static DesktopClientStartupResult LocalMode(string actorName)
    {
        var normalizedUserName = string.IsNullOrWhiteSpace(actorName) ? Environment.UserName : actorName.Trim();
        var roleCode = DesktopRoleCatalog.ResolveDefaultRoleCode(normalizedUserName);
        return new DesktopClientStartupResult(
            CanStart: true,
            UsesSharedDatabase: false,
            Message: "Клиент запущен в локальном режиме.",
            UserName: normalizedUserName,
            DisplayName: normalizedUserName,
            PrimaryRoleCode: roleCode,
            PrimaryRoleDisplayName: DesktopRoleCatalog.GetDisplayName(roleCode),
            RoleCodes: new[] { roleCode });
    }

    public static DesktopClientStartupResult SharedMode(string host, int port, string database, string user, DesktopAppUserProfile profile)
    {
        var roleCode = DesktopRoleCatalog.ResolvePrimaryRoleCode(profile.Roles, profile.UserName);
        return new DesktopClientStartupResult(
            CanStart: true,
            UsesSharedDatabase: true,
            Message: $"Клиент подключен к серверной БД {host}:{port}/{database} под пользователем {user}.",
            Host: host,
            Port: port,
            Database: database,
            User: user,
            UserName: profile.UserName,
            DisplayName: string.IsNullOrWhiteSpace(profile.DisplayName) ? profile.UserName : profile.DisplayName,
            PrimaryRoleCode: roleCode,
            PrimaryRoleDisplayName: DesktopRoleCatalog.GetDisplayName(roleCode),
            RoleCodes: new[] { roleCode });
    }

    public static DesktopClientStartupResult Failure(string message)
    {
        return new DesktopClientStartupResult(
            CanStart: false,
            UsesSharedDatabase: true,
            Message: message);
    }
}
