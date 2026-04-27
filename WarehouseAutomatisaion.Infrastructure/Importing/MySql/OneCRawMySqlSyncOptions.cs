namespace WarehouseAutomatisaion.Infrastructure.Importing.MySql;

public sealed class OneCRawMySqlSyncOptions
{
    public string WorkspaceRoot { get; init; } = WorkspacePathResolver.ResolveWorkspaceRoot();

    public string DatabaseName { get; init; } = "warehouse_automation";

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 3306;

    public string User { get; init; } = "root";

    public string Password { get; init; } = string.Empty;

    public string MysqlExecutablePath { get; init; } = string.Empty;

    public string SchemaPath { get; init; } = string.Empty;

    public string GeneratedScriptPath { get; init; } = string.Empty;

    public string CreatedBy { get; init; } = Environment.UserName;

    public bool RecreateDatabaseOnSync { get; init; }
}

public sealed record OneCRawMySqlSyncResult(
    string DatabaseName,
    string MysqlExecutablePath,
    string GeneratedScriptPath,
    long? BatchId,
    int ObjectCount,
    int FieldCount,
    int TabularRowCount,
    string Output);
