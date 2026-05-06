using System.ComponentModel;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class WarehouseOperationalWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };
    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;
    private DesktopModuleSnapshotMetadata? _remoteMetadata;

    public WarehouseOperationalWorkspaceStore(
        string storagePath,
        DesktopMySqlBackplaneService? backplane = null,
        bool serverModeEnabled = false)
    {
        StoragePath = storagePath;
        _backplane = backplane;
        _serverModeEnabled = serverModeEnabled;
    }

    public string StoragePath { get; }

    public bool IsRemoteDatabaseRequired => _serverModeEnabled;

    public bool IsServerModeEnabled => _serverModeEnabled && _backplane is not null;

    public static WarehouseOperationalWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new WarehouseOperationalWorkspaceStore(
            Path.Combine(root, "app_data", "warehouse-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault(),
            DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled());
    }

    public OperationalWarehouseWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        EnsureBackplaneReady(currentOperator);

        var workspace = OperationalWarehouseWorkspace.Create(currentOperator, salesWorkspace);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadWarehouseWorkspaceSnapshotRecord();
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            var persistedFromMySql = backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persistedFromMySql);
            return workspace;
        }

        var legacyBackplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<WarehouseWorkspaceSnapshot>("warehouse");
        if (legacyBackplaneRecord is not null)
        {
            var backplaneSnapshot = legacyBackplaneRecord.Snapshot;
            var persistedFromMySql = backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persistedFromMySql);
            TrySaveToBackplane(backplaneSnapshot, currentOperator);
            return workspace;
        }

        if (_serverModeEnabled)
        {
            return workspace;
        }

        if (!File.Exists(StoragePath))
        {
            return workspace;
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<WarehouseWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            var persisted = snapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persisted);
            var backplane = _backplane;
            var savedToBackplane = backplane is not null && TrySaveToBackplane(snapshot, currentOperator);
            if (!savedToBackplane && _serverModeEnabled)
            {
                throw CreateRemoteSaveException("склада");
            }

            return workspace;
        }
        catch
        {
            return workspace;
        }
    }

    public OperationalWarehouseWorkspace? TryLoadExisting(
        string currentOperator,
        IReadOnlyList<SalesCatalogItemOption>? catalogItems = null,
        IReadOnlyList<string>? warehouses = null)
    {
        EnsureBackplaneReady(currentOperator);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadWarehouseWorkspaceSnapshotRecord();
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            return backplaneSnapshot.ToWorkspace(currentOperator, catalogItems, warehouses);
        }

        var legacyBackplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<WarehouseWorkspaceSnapshot>("warehouse");
        if (legacyBackplaneRecord is not null)
        {
            var backplaneSnapshot = legacyBackplaneRecord.Snapshot;
            TrySaveToBackplane(backplaneSnapshot, currentOperator);
            return backplaneSnapshot.ToWorkspace(currentOperator, catalogItems, warehouses);
        }

        if (_serverModeEnabled)
        {
            return null;
        }

        if (!File.Exists(StoragePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<WarehouseWorkspaceSnapshot>(json, SerializerOptions);
            return snapshot?.ToWorkspace(currentOperator, catalogItems, warehouses);
        }
        catch
        {
            return null;
        }
    }

    public void Save(OperationalWarehouseWorkspace workspace)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        var snapshot = WarehouseWorkspaceSnapshot.FromWorkspace(workspace);
        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
        {
            return;
        }

        if (_serverModeEnabled)
        {
            throw CreateRemoteSaveException("склада");
        }

        var tempPath = $"{StoragePath}.tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024))
        {
            JsonSerializer.Serialize(stream, snapshot, SerializerOptions);
        }

        File.Move(tempPath, StoragePath, true);
    }

    private void EnsureBackplaneReady(string currentOperator)
    {
        if (!_serverModeEnabled)
        {
            return;
        }

        if (_backplane is null)
        {
            throw new InvalidOperationException("Включен режим общей БД, но подключение к серверу недоступно. Локальная загрузка склада отключена.");
        }

        _backplane.EnsureReady(currentOperator);
    }

    private static InvalidOperationException CreateRemoteSaveException(string moduleName)
    {
        return new InvalidOperationException($"Не удалось сохранить данные {moduleName} в серверную БД. Локальное сохранение отключено для общего режима.");
    }

    private bool TrySaveToBackplane(WarehouseWorkspaceSnapshot snapshot, string currentOperator)
    {
        if (_backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = _backplane.TrySaveWarehouseWorkspaceSnapshot(snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded)
        {
            _remoteMetadata = result.Metadata;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = _backplane.TryLoadWarehouseWorkspaceSnapshotRecord();
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        var retry = _backplane.TrySaveWarehouseWorkspaceSnapshot(merged, currentOperator, latest.Metadata, CreateAuditSeeds(merged.OperationLog));
        if (!retry.Succeeded)
        {
            throw new InvalidOperationException("Данные склада на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        return true;
    }

    private static IReadOnlyList<DesktopAuditEventSeed> CreateAuditSeeds(IEnumerable<WarehouseOperationLogEntry> entries)
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

    private static void MergeWorkspace(OperationalWarehouseWorkspace target, OperationalWarehouseWorkspace persisted)
    {
        MergeDocuments(target.TransferOrders, persisted.TransferOrders);
        MergeDocuments(target.InventoryCounts, persisted.InventoryCounts);
        MergeDocuments(target.WriteOffs, persisted.WriteOffs);
        MergeStorageCells(target.StorageCells, persisted.StorageCells);
        MergeOperationLog(target.OperationLog, persisted.OperationLog);

        target.CurrentOperator = string.IsNullOrWhiteSpace(persisted.CurrentOperator)
            ? target.CurrentOperator
            : persisted.CurrentOperator;
        target.Warehouses = target.Warehouses
            .Concat(persisted.Warehouses)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        target.EnsureDefaultStorageCells(raiseChanged: false);
    }

    private static void MergeDocuments(
        BindingList<OperationalWarehouseDocumentRecord> target,
        IEnumerable<OperationalWarehouseDocumentRecord> persistedDocuments)
    {
        var targetByKey = target
            .Select(item => (Key: BuildDocumentKey(item), Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var persisted in persistedDocuments)
        {
            var key = BuildDocumentKey(persisted);
            if (string.IsNullOrWhiteSpace(key))
            {
                target.Add(persisted.Clone());
                continue;
            }

            if (targetByKey.TryGetValue(key, out var current))
            {
                current.CopyFrom(MergeDocument(current, persisted));
                continue;
            }

            var clone = persisted.Clone();
            target.Add(clone);
            targetByKey[key] = clone;
        }
    }

    private static void MergeOperationLog(
        BindingList<WarehouseOperationLogEntry> target,
        IEnumerable<WarehouseOperationLogEntry> persistedEntries)
    {
        var merged = target
            .Concat(persistedEntries.Select(item => item.Clone()))
            .GroupBy(item => item.Id)
            .Select(group => group
                .OrderByDescending(item => item.LoggedAt)
                .First())
            .OrderByDescending(item => item.LoggedAt)
            .Take(500)
            .ToArray();

        target.Clear();
        foreach (var entry in merged)
        {
            target.Add(entry);
        }
    }

    private static void MergeStorageCells(
        BindingList<WarehouseStorageCellRecord> target,
        IEnumerable<WarehouseStorageCellRecord> persistedCells)
    {
        var targetByKey = target
            .Select(item => (Key: BuildStorageCellKey(item), Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var persisted in persistedCells)
        {
            var key = BuildStorageCellKey(persisted);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (targetByKey.TryGetValue(key, out var current))
            {
                CopyCell(current, persisted);
                continue;
            }

            var clone = persisted.Clone();
            target.Add(clone);
            targetByKey[key] = clone;
        }
    }

    private static void CopyCell(WarehouseStorageCellRecord target, WarehouseStorageCellRecord source)
    {
        target.Id = source.Id == Guid.Empty ? target.Id : source.Id;
        target.Warehouse = FirstNonEmpty(source.Warehouse, target.Warehouse);
        target.Code = FirstNonEmpty(source.Code, target.Code);
        target.ZoneCode = FirstNonEmpty(source.ZoneCode, target.ZoneCode);
        target.ZoneName = FirstNonEmpty(source.ZoneName, target.ZoneName);
        target.Row = source.Row != 0 ? source.Row : target.Row;
        target.Rack = source.Rack != 0 ? source.Rack : target.Rack;
        target.Shelf = source.Shelf != 0 ? source.Shelf : target.Shelf;
        target.Cell = source.Cell != 0 ? source.Cell : target.Cell;
        target.CellType = FirstNonEmpty(source.CellType, target.CellType);
        target.Capacity = source.Capacity > 0m ? source.Capacity : target.Capacity;
        target.Status = FirstNonEmpty(source.Status, target.Status);
        target.QrPayload = FirstNonEmpty(source.QrPayload, target.QrPayload);
        target.Comment = FirstNonEmpty(source.Comment, target.Comment);
    }

    private static OperationalWarehouseDocumentRecord MergeDocument(
        OperationalWarehouseDocumentRecord runtime,
        OperationalWarehouseDocumentRecord persisted)
    {
        var merged = runtime.Clone();
        merged.Id = persisted.Id != Guid.Empty ? persisted.Id : runtime.Id;
        merged.DocumentType = FirstNonEmpty(persisted.DocumentType, runtime.DocumentType);
        merged.Number = FirstNonEmpty(persisted.Number, runtime.Number);
        merged.DocumentDate = persisted.DocumentDate != default ? persisted.DocumentDate : runtime.DocumentDate;
        merged.Status = FirstNonEmpty(persisted.Status, runtime.Status);
        merged.SourceWarehouse = FirstNonEmpty(persisted.SourceWarehouse, runtime.SourceWarehouse);
        merged.TargetWarehouse = FirstNonEmpty(persisted.TargetWarehouse, runtime.TargetWarehouse);
        merged.RelatedDocument = FirstNonEmpty(persisted.RelatedDocument, runtime.RelatedDocument);
        merged.Comment = FirstNonEmpty(persisted.Comment, runtime.Comment);
        merged.SourceLabel = MergeSourceLabels(runtime.SourceLabel, persisted.SourceLabel);
        merged.Fields = ChoosePreferredFields(runtime.Fields, persisted.Fields, persisted.SourceLabel);
        merged.Lines = ChoosePreferredLines(runtime.Lines, persisted.Lines, persisted.SourceLabel);
        return merged;
    }

    private static IReadOnlyList<OneCFieldValue> ChoosePreferredFields(
        IReadOnlyList<OneCFieldValue> runtimeFields,
        IReadOnlyList<OneCFieldValue> persistedFields,
        string persistedSourceLabel)
    {
        return persistedFields.Count > runtimeFields.Count || IsLocalSource(persistedSourceLabel)
            ? persistedFields.ToArray()
            : runtimeFields.ToArray();
    }

    private static BindingList<OperationalWarehouseLineRecord> ChoosePreferredLines(
        BindingList<OperationalWarehouseLineRecord> runtimeLines,
        BindingList<OperationalWarehouseLineRecord> persistedLines,
        string persistedSourceLabel)
    {
        var source = persistedLines.Count > runtimeLines.Count || IsLocalSource(persistedSourceLabel)
            ? persistedLines
            : runtimeLines;
        return new BindingList<OperationalWarehouseLineRecord>(source.Select(item => item.Clone()).ToList());
    }

    private static string BuildDocumentKey(OperationalWarehouseDocumentRecord document)
    {
        return !string.IsNullOrWhiteSpace(document.Number)
            ? $"{document.DocumentType}|{document.Number}"
            : $"{document.DocumentType}|{document.SourceWarehouse}|{document.TargetWarehouse}|{document.DocumentDate:yyyyMMdd}";
    }

    private static string BuildStorageCellKey(WarehouseStorageCellRecord cell)
    {
        return OperationalWarehouseWorkspace.BuildStorageCellKey(cell);
    }

    private static bool IsLocalSource(string sourceLabel)
    {
        return sourceLabel.Contains("локаль", StringComparison.OrdinalIgnoreCase)
               || sourceLabel.Contains("desktop", StringComparison.OrdinalIgnoreCase)
               || sourceLabel.Contains("контур", StringComparison.OrdinalIgnoreCase);
    }

    private static string MergeSourceLabels(string runtimeSource, string persistedSource)
    {
        if (string.IsNullOrWhiteSpace(runtimeSource))
        {
            return persistedSource;
        }

        if (string.IsNullOrWhiteSpace(persistedSource)
            || runtimeSource.Equals(persistedSource, StringComparison.OrdinalIgnoreCase))
        {
            return runtimeSource;
        }

        return IsLocalSource(persistedSource)
            ? persistedSource
            : $"{runtimeSource} + {persistedSource}";
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static WarehouseWorkspaceSnapshot MergeSnapshots(WarehouseWorkspaceSnapshot server, WarehouseWorkspaceSnapshot local)
    {
        return new WarehouseWorkspaceSnapshot
        {
            CurrentOperator = string.IsNullOrWhiteSpace(local.CurrentOperator) ? server.CurrentOperator : local.CurrentOperator,
            TransferOrders = MergeRecords(server.TransferOrders, local.TransferOrders, BuildDocumentKey, item => item.Clone()),
            InventoryCounts = MergeRecords(server.InventoryCounts, local.InventoryCounts, BuildDocumentKey, item => item.Clone()),
            WriteOffs = MergeRecords(server.WriteOffs, local.WriteOffs, BuildDocumentKey, item => item.Clone()),
            StorageCells = MergeRecords(server.StorageCells, local.StorageCells, BuildStorageCellKey, item => item.Clone()),
            OperationLog = server.OperationLog
                .Concat(local.OperationLog)
                .GroupBy(item => item.Id == Guid.Empty ? $"{item.EntityType}|{item.EntityNumber}|{item.Action}|{item.LoggedAt:O}" : item.Id.ToString("N"), StringComparer.OrdinalIgnoreCase)
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
            if (!string.IsNullOrWhiteSpace(key))
            {
                indexes[key] = merged.Count;
            }

            merged.Add(clone(item));
        }

        foreach (var item in local)
        {
            var key = keySelector(item);
            var cloned = clone(item);
            if (!string.IsNullOrWhiteSpace(key) && indexes.TryGetValue(key, out var index))
            {
                merged[index] = cloned;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key))
            {
                indexes[key] = merged.Count;
            }

            merged.Add(cloned);
        }

        return merged;
    }

    internal sealed class WarehouseWorkspaceSnapshot
    {
        public string CurrentOperator { get; set; } = string.Empty;

        public List<OperationalWarehouseDocumentRecord> TransferOrders { get; set; } = [];

        public List<OperationalWarehouseDocumentRecord> InventoryCounts { get; set; } = [];

        public List<OperationalWarehouseDocumentRecord> WriteOffs { get; set; } = [];

        public List<WarehouseStorageCellRecord> StorageCells { get; set; } = [];

        public List<WarehouseOperationLogEntry> OperationLog { get; set; } = [];

        public static WarehouseWorkspaceSnapshot FromWorkspace(OperationalWarehouseWorkspace workspace)
        {
            return new WarehouseWorkspaceSnapshot
            {
                CurrentOperator = workspace.CurrentOperator,
                TransferOrders = workspace.TransferOrders.Select(item => item.Clone()).ToList(),
                InventoryCounts = workspace.InventoryCounts.Select(item => item.Clone()).ToList(),
                WriteOffs = workspace.WriteOffs.Select(item => item.Clone()).ToList(),
                StorageCells = workspace.StorageCells.Select(item => item.Clone()).ToList(),
                OperationLog = workspace.OperationLog.Select(item => item.Clone()).ToList()
            };
        }

        public OperationalWarehouseWorkspace ToWorkspace(
            string currentOperator,
            IReadOnlyList<SalesCatalogItemOption>? catalogItems,
            IReadOnlyList<string>? warehouses)
        {
            var workspace = OperationalWarehouseWorkspace.CreateEmpty(
                string.IsNullOrWhiteSpace(CurrentOperator) ? currentOperator : CurrentOperator,
                catalogItems,
                warehouses);

            foreach (var transfer in TransferOrders)
            {
                workspace.TransferOrders.Add(transfer.Clone());
            }

            foreach (var inventory in InventoryCounts)
            {
                workspace.InventoryCounts.Add(inventory.Clone());
            }

            foreach (var writeOff in WriteOffs)
            {
                workspace.WriteOffs.Add(writeOff.Clone());
            }

            MergeStorageCells(workspace.StorageCells, StorageCells);

            foreach (var logEntry in OperationLog)
            {
                workspace.OperationLog.Add(logEntry.Clone());
            }

            workspace.EnsureDefaultStorageCells(raiseChanged: false);
            return workspace;
        }
    }
}
