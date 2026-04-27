using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Infrastructure.Projection.MySql;

public sealed class OneCOperationalProjectionOptions
{
    public string WorkspaceRoot { get; init; } = WorkspacePathResolver.ResolveWorkspaceRoot();

    public string DatabaseName { get; init; } = "warehouse_automation_raw_dev";

    public string Host { get; init; } = "127.0.0.1";

    public int Port { get; init; } = 3306;

    public string User { get; init; } = "root";

    public string Password { get; init; } = string.Empty;

    public string MysqlExecutablePath { get; init; } = string.Empty;

    public string GeneratedScriptPath { get; init; } = string.Empty;

    public bool RebuildOperationalTables { get; init; } = true;
}

public sealed record OneCOperationalProjectionResult(
    string DatabaseName,
    string MysqlExecutablePath,
    string GeneratedScriptPath,
    int OrganizationCount,
    int PartnerCount,
    int ItemCount,
    int SalesInvoiceCount,
    int PurchaseOrderCount,
    int PurchaseReceiptCount,
    string Output);
