using WarehouseAutomatisaion.Infrastructure.Importing;
using WarehouseAutomatisaion.Infrastructure.Importing.MySql;
using WarehouseAutomatisaion.Infrastructure.Projection.MySql;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class OneCLiveSyncDesktopService
{
    private readonly SalesWorkspaceStore _workspaceStore;
    private readonly OneCLiveRuntimeExportService _exportService;

    public OneCLiveSyncDesktopService(
        SalesWorkspaceStore workspaceStore,
        OneCLiveRuntimeExportService exportService)
    {
        _workspaceStore = workspaceStore;
        _exportService = exportService;
    }

    public static OneCLiveSyncDesktopService CreateDefault(SalesWorkspaceStore? workspaceStore = null)
    {
        return new OneCLiveSyncDesktopService(
            workspaceStore ?? SalesWorkspaceStore.CreateDefault(),
            OneCLiveRuntimeExportService.CreateDefault());
    }

    public async Task<OneCLiveSyncDesktopResult> SyncAsync(string currentOperator, CancellationToken cancellationToken = default)
    {
        var exportResult = await _exportService.ExportCriticalSnapshotAsync(cancellationToken);
        var importRoots = BuildPreferredImportRoots(exportResult.OutputRoot);
        var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot();

        var importSnapshot = await Task.Run(
            () => new OneCImportService(workspaceRoot, importRoots).LoadSnapshot(),
            cancellationToken);

        var mySqlRefresh = await Task.Run(
            () => TryRefreshMySqlProjection(importSnapshot, workspaceRoot, currentOperator),
            cancellationToken);

        SalesWorkspace workspace;
        if (mySqlRefresh.Succeeded)
        {
            workspace = await Task.Run(
                () => _workspaceStore.LoadOrCreate(currentOperator),
                cancellationToken);
        }
        else
        {
            workspace = await Task.Run(
                () => _workspaceStore.LoadOrCreate(
                    currentOperator,
                    includeOperationalSnapshot: false,
                    importRoots),
                cancellationToken);
        }

        return new OneCLiveSyncDesktopResult(exportResult, workspace, mySqlRefresh);
    }

    private static OneCMySqlRefreshDesktopResult TryRefreshMySqlProjection(
        OneCImportSnapshot importSnapshot,
        string workspaceRoot,
        string currentOperator)
    {
        try
        {
            var sharedOptions = CreateSharedMySqlOptions(workspaceRoot, currentOperator);
            var rawSyncService = new OneCRawSnapshotMySqlSyncService(new OneCRawMySqlSyncOptions
            {
                WorkspaceRoot = workspaceRoot,
                DatabaseName = sharedOptions.DatabaseName,
                Host = sharedOptions.Host,
                Port = sharedOptions.Port,
                User = sharedOptions.User,
                Password = sharedOptions.Password,
                MysqlExecutablePath = sharedOptions.MysqlExecutablePath,
                CreatedBy = currentOperator,
                RecreateDatabaseOnSync = false
            });

            var rawSyncResult = rawSyncService.SyncSnapshot(importSnapshot);

            var projectionService = new OneCOperationalProjectionService(new OneCOperationalProjectionOptions
            {
                WorkspaceRoot = workspaceRoot,
                DatabaseName = sharedOptions.DatabaseName,
                Host = sharedOptions.Host,
                Port = sharedOptions.Port,
                User = sharedOptions.User,
                Password = sharedOptions.Password,
                MysqlExecutablePath = sharedOptions.MysqlExecutablePath,
                RebuildOperationalTables = true
            });

            var projectionResult = projectionService.ProjectLatestRawBatch();
            return OneCMySqlRefreshDesktopResult.CreateSuccess(rawSyncResult, projectionResult);
        }
        catch (Exception exception)
        {
            return OneCMySqlRefreshDesktopResult.CreateFailure(exception.Message);
        }
    }

    private static OperationalMySqlDesktopOptions CreateSharedMySqlOptions(string workspaceRoot, string currentOperator)
    {
        return new OperationalMySqlDesktopOptions
        {
            Host = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_HOST") ?? "127.0.0.1",
            Port = int.TryParse(Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PORT"), out var port) ? port : 3306,
            DatabaseName = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_DATABASE") ?? "warehouse_automation_raw_dev",
            User = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_USER") ?? "root",
            Password = Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_PASSWORD") ?? string.Empty,
            MysqlExecutablePath =
                Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_EXECUTABLE")
                ?? Environment.GetEnvironmentVariable("WAREHOUSE_MYSQL_EXE")
                ?? string.Empty
        };
    }

    private static IReadOnlyList<string> BuildPreferredImportRoots(string primaryRoot)
    {
        var roots = new List<string>();

        if (Directory.Exists(primaryRoot) && File.Exists(Path.Combine(primaryRoot, "manifest.csv")))
        {
            roots.Add(primaryRoot);
        }

        var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot();
        var liveRoot = Path.Combine(workspaceRoot, "app_data", "one-c-live");
        if (Directory.Exists(liveRoot))
        {
            roots.AddRange(
                Directory
                    .GetDirectories(liveRoot)
                    .Where(path => !IsIgnoredImportRoot(path))
                    .Where(path => !string.Equals(path, primaryRoot, StringComparison.OrdinalIgnoreCase))
                    .Where(path => File.Exists(Path.Combine(path, "manifest.csv")))
                    .OrderByDescending(Directory.GetLastWriteTimeUtc));
        }

        if (Directory.Exists(workspaceRoot))
        {
            roots.AddRange(
                Directory
                    .GetDirectories(workspaceRoot, "exports*")
                    .Where(path => File.Exists(Path.Combine(path, "manifest.csv")))
                    .OrderByDescending(Directory.GetLastWriteTimeUtc));
        }

        return roots
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsIgnoredImportRoot(string path)
    {
        var name = Path.GetFileName(path);
        return name.StartsWith("smoke-", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record OneCLiveSyncDesktopResult(
    OneCLiveRuntimeExportResult ExportResult,
    SalesWorkspace Workspace,
    OneCMySqlRefreshDesktopResult MySqlRefresh);

public sealed record OneCMySqlRefreshDesktopResult(
    bool Succeeded,
    string Mode,
    string DiagnosticMessage,
    OneCRawMySqlSyncResult? RawSyncResult,
    OneCOperationalProjectionResult? ProjectionResult)
{
    public static OneCMySqlRefreshDesktopResult CreateSuccess(
        OneCRawMySqlSyncResult rawSyncResult,
        OneCOperationalProjectionResult projectionResult)
    {
        return new OneCMySqlRefreshDesktopResult(
            true,
            "MySQL",
            "Live-данные 1С перенесены в raw и operational MySQL.",
            rawSyncResult,
            projectionResult);
    }

    public static OneCMySqlRefreshDesktopResult CreateFailure(string diagnosticMessage)
    {
        return new OneCMySqlRefreshDesktopResult(
            false,
            "FallbackImport",
            diagnosticMessage,
            null,
            null);
    }
}
