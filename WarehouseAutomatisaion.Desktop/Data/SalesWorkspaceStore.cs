using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class SalesWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;

    public SalesWorkspaceStore(
        string storagePath,
        DesktopMySqlBackplaneService? backplane = null,
        bool serverModeEnabled = false)
    {
        StoragePath = storagePath;
        _backplane = backplane;
        _serverModeEnabled = serverModeEnabled;
    }

    public string StoragePath { get; }

    public static SalesWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        var storagePath = Path.Combine(root, "app_data", "sales-workspace.json");
        return new SalesWorkspaceStore(
            storagePath,
            DesktopMySqlBackplaneService.TryCreateDefault(),
            DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled());
    }

    public SalesWorkspace LoadOrCreate(
        string currentOperator,
        bool includeOperationalSnapshot = true,
        IReadOnlyList<string>? importRoots = null)
    {
        var workspace = SalesWorkspace.Create(currentOperator);
        var shouldAttachImportSnapshot = importRoots is { Count: > 0 };
        if (shouldAttachImportSnapshot)
        {
            AttachImportSnapshot(workspace, importRoots);
        }
        else
        {
            workspace.AttachOneCImportSnapshot(null);
        }
        DesktopOperationalSnapshot? operationalSnapshot = null;
        if (includeOperationalSnapshot)
        {
            operationalSnapshot = AttachOperationalSnapshot(workspace);
            if (operationalSnapshot?.HasSalesData == true)
            {
                ApplyOperationalSnapshot(workspace, operationalSnapshot, currentOperator);
            }
        }
        else
        {
            workspace.AttachOperationalSnapshot(null);
        }

        _backplane?.TryEnsureUserProfile(currentOperator);
        SalesWorkspaceImportMerger.Merge(workspace);
        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<SalesWorkspaceSnapshot>("sales");
        if (backplaneSnapshot is not null)
        {
            ApplySnapshotToWorkspace(workspace, backplaneSnapshot, operationalSnapshot, importRoots);
            return workspace;
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<SalesWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            if (operationalSnapshot?.HasSalesData == true)
            {
                MergeSnapshotIntoOperationalWorkspace(workspace, snapshot);
            }
            else
            {
                ApplySnapshot(workspace, snapshot);
                if (importRoots is { Count: > 0 })
                {
                    AttachImportSnapshot(workspace, importRoots);
                    SalesWorkspaceImportMerger.Merge(workspace);
                }
                else
                {
                    workspace.AttachOneCImportSnapshot(null);
                }
            }

            _backplane?.TrySaveModuleSnapshot("sales", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog));
            return workspace;
        }
        catch
        {
            return workspace;
        }
    }

    public void Save(SalesWorkspace workspace)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        var snapshot = SalesWorkspaceSnapshot.FromWorkspace(workspace);
        if (_backplane?.TrySaveModuleSnapshot("sales", snapshot, workspace.CurrentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
        {
            return;
        }

        if (_serverModeEnabled)
        {
            throw new InvalidOperationException("Не удалось сохранить изменения в серверную БД. Локальное сохранение отключено для общего режима.");
        }

        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
    }

    private static void ApplySnapshotToWorkspace(
        SalesWorkspace workspace,
        SalesWorkspaceSnapshot snapshot,
        DesktopOperationalSnapshot? operationalSnapshot,
        IReadOnlyList<string>? importRoots)
    {
        if (operationalSnapshot?.HasSalesData == true)
        {
            MergeSnapshotIntoOperationalWorkspace(workspace, snapshot);
            return;
        }

        ApplySnapshot(workspace, snapshot);
        if (importRoots is { Count: > 0 })
        {
            AttachImportSnapshot(workspace, importRoots);
            SalesWorkspaceImportMerger.Merge(workspace);
        }
        else
        {
            workspace.AttachOneCImportSnapshot(null);
        }
    }

    private static void ApplySnapshot(SalesWorkspace workspace, SalesWorkspaceSnapshot snapshot)
    {
        ReplaceList(workspace.Customers, snapshot.Customers, item => item.Clone());
        ReplaceList(workspace.Orders, snapshot.Orders, item => item.Clone());
        ReplaceList(workspace.Invoices, snapshot.Invoices, item => item.Clone());
        ReplaceList(workspace.Shipments, snapshot.Shipments, item => item.Clone());
        ReplaceList(workspace.Returns, snapshot.Returns, item => item.Clone());
        ReplaceList(workspace.CashReceipts, snapshot.CashReceipts, item => item.Clone());
        ReplaceList(workspace.OperationLog, snapshot.OperationLog, item => item.Clone());
    }

    private static void ApplyOperationalSnapshot(
        SalesWorkspace workspace,
        DesktopOperationalSnapshot snapshot,
        string currentOperator)
    {
        ReplaceList(workspace.Customers, snapshot.Customers, item => item.Clone());
        ReplaceList(workspace.Orders, snapshot.Orders, item => item.Clone());
        ReplaceList(workspace.Invoices, snapshot.Invoices, item => item.Clone());
        ReplaceList(workspace.Shipments, snapshot.Shipments, item => item.Clone());
        ReplaceList(workspace.Returns, Array.Empty<SalesReturnRecord>(), item => item.Clone());
        ReplaceList(workspace.CashReceipts, Array.Empty<SalesCashReceiptRecord>(), item => item.Clone());

        if (snapshot.CatalogItems.Count > 0)
        {
            workspace.CatalogItems = snapshot.CatalogItems
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        workspace.Managers = BuildLookupList(snapshot.Managers, workspace.Managers, currentOperator);
        workspace.Currencies = BuildLookupList(snapshot.Currencies, workspace.Currencies, "RUB");
        workspace.Warehouses = BuildLookupList(snapshot.Warehouses, workspace.Warehouses);
    }

    private static void MergeSnapshotIntoOperationalWorkspace(SalesWorkspace workspace, SalesWorkspaceSnapshot snapshot)
    {
        var knownCustomerIds = workspace.Customers
            .Select(item => item.Id)
            .ToHashSet();

        MergeOrders(workspace.Orders, snapshot.Orders, knownCustomerIds);
        MergeInvoices(workspace.Invoices, snapshot.Invoices, knownCustomerIds);
        MergeShipments(workspace.Shipments, snapshot.Shipments, knownCustomerIds);
        MergeReturns(workspace.Returns, snapshot.Returns, knownCustomerIds);
        MergeCashReceipts(workspace.CashReceipts, snapshot.CashReceipts, knownCustomerIds);
        ReplaceList(workspace.OperationLog, snapshot.OperationLog, item => item.Clone());
    }

    private static void ReplaceList<T>(ICollection<T> target, IEnumerable<T>? source, Func<T, T> clone)
    {
        target.Clear();
        if (source is null)
        {
            return;
        }

        foreach (var item in source)
        {
            target.Add(clone(item));
        }
    }

    private static void AttachImportSnapshot(SalesWorkspace workspace, IReadOnlyList<string>? importRoots = null)
    {
        try
        {
            var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot();
            var importService = importRoots is { Count: > 0 }
                ? new OneCImportService(workspaceRoot, importRoots)
                : new OneCImportService(workspaceRoot);
            workspace.AttachOneCImportSnapshot(importService.LoadSnapshot());
        }
        catch
        {
            workspace.AttachOneCImportSnapshot(null);
        }
    }

    private static DesktopOperationalSnapshot? AttachOperationalSnapshot(SalesWorkspace workspace)
    {
        try
        {
            var snapshot = OperationalMySqlDesktopService.TryCreateConfigured()?.TryLoadSnapshot();
            workspace.AttachOperationalSnapshot(snapshot);
            return snapshot;
        }
        catch (Exception exception)
        {
            try
            {
                var root = WorkspacePathResolver.ResolveWorkspaceRoot();
                var path = Path.Combine(root, "app_data", "operational-desktop-attach-error.log");
                File.WriteAllText(path, exception.ToString(), Encoding.UTF8);
            }
            catch
            {
            }

            workspace.AttachOperationalSnapshot(null);
            return null;
        }
    }

    private static void MergeOrders(
        ICollection<SalesOrderRecord> target,
        IEnumerable<SalesOrderRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeInvoices(
        ICollection<SalesInvoiceRecord> target,
        IEnumerable<SalesInvoiceRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeShipments(
        ICollection<SalesShipmentRecord> target,
        IEnumerable<SalesShipmentRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeReturns(
        ICollection<SalesReturnRecord> target,
        IEnumerable<SalesReturnRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static void MergeCashReceipts(
        ICollection<SalesCashReceiptRecord> target,
        IEnumerable<SalesCashReceiptRecord> source,
        IReadOnlySet<Guid> knownCustomerIds)
    {
        var targetByNumber = target
            .Where(item => !string.IsNullOrWhiteSpace(item.Number))
            .ToDictionary(item => item.Number, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            if (!knownCustomerIds.Contains(item.CustomerId) || string.IsNullOrWhiteSpace(item.Number))
            {
                continue;
            }

            if (targetByNumber.TryGetValue(item.Number, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByNumber[clone.Number] = clone;
        }
    }

    private static IReadOnlyList<string> BuildLookupList(
        IEnumerable<string> preferredValues,
        IReadOnlyList<string> fallbackValues,
        params string[] enforcedValues)
    {
        return preferredValues
            .Concat(fallbackValues)
            .Concat(enforcedValues)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DesktopAuditEventSeed> CreateAuditSeeds(IEnumerable<SalesOperationLogEntry> entries)
    {
        return entries
            .Select(item => new DesktopAuditEventSeed(
                item.Id,
                item.LoggedAt.Kind == DateTimeKind.Utc ? item.LoggedAt : item.LoggedAt.ToUniversalTime(),
                item.Actor,
                item.EntityType,
                item.EntityId,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message))
            .ToArray();
    }
}

public sealed class SalesWorkspaceSnapshot
{
    public List<SalesCustomerRecord> Customers { get; set; } = [];

    public List<SalesOrderRecord> Orders { get; set; } = [];

    public List<SalesInvoiceRecord> Invoices { get; set; } = [];

    public List<SalesShipmentRecord> Shipments { get; set; } = [];

    public List<SalesReturnRecord> Returns { get; set; } = [];

    public List<SalesCashReceiptRecord> CashReceipts { get; set; } = [];

    public List<SalesOperationLogEntry> OperationLog { get; set; } = [];

    public static SalesWorkspaceSnapshot FromWorkspace(SalesWorkspace workspace)
    {
        return new SalesWorkspaceSnapshot
        {
            Customers = workspace.Customers.Select(item => item.Clone()).ToList(),
            Orders = workspace.Orders.Select(item => item.Clone()).ToList(),
            Invoices = workspace.Invoices.Select(item => item.Clone()).ToList(),
            Shipments = workspace.Shipments.Select(item => item.Clone()).ToList(),
            Returns = workspace.Returns.Select(item => item.Clone()).ToList(),
            CashReceipts = workspace.CashReceipts.Select(item => item.Clone()).ToList(),
            OperationLog = workspace.OperationLog.Select(item => item.Clone()).ToList()
        };
    }
}
