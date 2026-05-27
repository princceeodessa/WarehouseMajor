namespace WarehouseAutomatisaion.Infrastructure.Options;

// Options для прямой работы с прод MySQL (warehouse_automation).
// Биндится в appsettings: "RemoteDatabase": { Enabled, Host, Port, Database, User, Password }.
// Это тот же ключ что использует Tsd (RemoteDatabaseOptions) — единая секция.
public sealed class MySqlPersistenceOptions
{
    public const string SectionName = "RemoteDatabase";

    public bool Enabled { get; set; }

    public string Host { get; set; } = "127.0.0.1";

    public int Port { get; set; } = 3306;

    public string Database { get; set; } = string.Empty;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public bool HasCompleteConnection =>
        Enabled
        && !string.IsNullOrWhiteSpace(Host)
        && Port > 0
        && !string.IsNullOrWhiteSpace(Database)
        && !string.IsNullOrWhiteSpace(User);
}
