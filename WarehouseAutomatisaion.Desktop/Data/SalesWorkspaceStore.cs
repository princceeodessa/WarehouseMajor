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
    private DesktopModuleSnapshotMetadata? _remoteMetadata;

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

    public bool IsServerModeEnabled => _serverModeEnabled && _backplane is not null;

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
        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (backplaneRecord is not null)
        {
            _remoteMetadata = backplaneRecord.Metadata;
            ApplySnapshotToWorkspace(workspace, backplaneRecord.Snapshot, operationalSnapshot, importRoots);
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

            if (_backplane?.TrySaveModuleSnapshot("sales", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
            {
                _remoteMetadata = _backplane.TryLoadModuleSnapshotMetadata("sales");
            }

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
        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
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

    public bool HasRemoteChanges()
    {
        if (_backplane is null || _remoteMetadata is null)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotMetadata("sales");
        return latest is not null
               && (!string.Equals(latest.PayloadHash, _remoteMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase)
                   || latest.VersionNo != _remoteMetadata.VersionNo);
    }

    public bool TryRefreshFromBackplane(SalesWorkspace workspace)
    {
        var record = _backplane?.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (record is null)
        {
            return false;
        }

        if (_remoteMetadata is not null
            && record.Metadata.VersionNo == _remoteMetadata.VersionNo
            && string.Equals(record.Metadata.PayloadHash, _remoteMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        ApplySnapshotToWorkspace(workspace, record.Snapshot, workspace.OperationalSnapshot, importRoots: null);
        _remoteMetadata = record.Metadata;
        workspace.NotifyExternalChange();
        return true;
    }

    private bool TrySaveToBackplane(SalesWorkspaceSnapshot snapshot, string currentOperator)
    {
        if (_backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = _backplane.TrySaveModuleSnapshot("sales", snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded)
        {
            _remoteMetadata = result.Metadata;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotRecord<SalesWorkspaceSnapshot>("sales");
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        var mergedAuditEvents = CreateAuditSeeds(merged.OperationLog);
        var retry = _backplane.TrySaveModuleSnapshot("sales", merged, currentOperator, latest.Metadata, mergedAuditEvents);
        if (!retry.Succeeded)
        {
            throw new InvalidOperationException("Данные на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        return true;
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

    private static SalesWorkspaceSnapshot MergeSnapshots(SalesWorkspaceSnapshot server, SalesWorkspaceSnapshot local)
    {
        return new SalesWorkspaceSnapshot
        {
            Customers = MergeRecords(server.Customers, local.Customers, BuildCustomerKey, item => item.Clone()),
            Orders = MergeRecords(server.Orders, local.Orders, BuildOrderKey, item => item.Clone()),
            Invoices = MergeRecords(server.Invoices, local.Invoices, BuildInvoiceKey, item => item.Clone()),
            Shipments = MergeRecords(server.Shipments, local.Shipments, BuildShipmentKey, item => item.Clone()),
            Returns = MergeRecords(server.Returns, local.Returns, BuildReturnKey, item => item.Clone()),
            CashReceipts = MergeRecords(server.CashReceipts, local.CashReceipts, BuildCashReceiptKey, item => item.Clone()),
            OperationLog = server.OperationLog
                .Concat(local.OperationLog)
                .GroupBy(item => item.Id == Guid.Empty ? CreateFallbackLogKey(item) : item.Id.ToString("N"), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LoggedAt).First().Clone())
                .OrderByDescending(item => item.LoggedAt)
                .Take(500)
                .ToList()
        };
    }

    private static List<T> MergeRecords<T>(
        IEnumerable<T> server,
        IEnumerable<T> local,
        Func<T, string> keySelector,
        Func<T, T> clone)
    {
        var merged = new List<T>();
        var indexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in server)
        {
            var key = keySelector(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                merged.Add(clone(item));
                continue;
            }

            indexes[key] = merged.Count;
            merged.Add(clone(item));
        }

        foreach (var item in local)
        {
            var key = keySelector(item);
            var cloneItem = clone(item);
            if (!string.IsNullOrWhiteSpace(key) && indexes.TryGetValue(key, out var index))
            {
                merged[index] = cloneItem;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                indexes[key] = merged.Count;
            }

            merged.Add(cloneItem);
        }

        return merged;
    }

    private static string BuildCustomerKey(SalesCustomerRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : !string.IsNullOrWhiteSpace(item.Code)
                ? $"code:{item.Code}"
                : $"name:{item.Name}";
    }

    private static string BuildOrderKey(SalesOrderRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildInvoiceKey(SalesInvoiceRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildShipmentKey(SalesShipmentRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildReturnKey(SalesReturnRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string BuildCashReceiptKey(SalesCashReceiptRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : $"number:{item.Number}";
    }

    private static string CreateFallbackLogKey(SalesOperationLogEntry item)
    {
        return $"{item.EntityType}|{item.EntityId:N}|{item.EntityNumber}|{item.Action}|{item.LoggedAt:O}";
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
        MergeCustomers(workspace.Customers, snapshot.Customers);

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

    private static void MergeCustomers(
        ICollection<SalesCustomerRecord> target,
        IEnumerable<SalesCustomerRecord> source)
    {
        var targetByKey = target
            .Select(item => (Key: BuildCustomerKey(item), Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var item in source)
        {
            var key = BuildCustomerKey(item);
            if (string.IsNullOrWhiteSpace(key))
            {
                target.Add(item.Clone());
                continue;
            }

            if (targetByKey.TryGetValue(key, out var existing))
            {
                existing.CopyFrom(item);
                continue;
            }

            var clone = item.Clone();
            target.Add(clone);
            targetByKey[key] = clone;
        }
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
