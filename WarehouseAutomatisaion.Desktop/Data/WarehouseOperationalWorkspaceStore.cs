using System.ComponentModel;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class WarehouseOperationalWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly DesktopMySqlBackplaneService? _backplane;

    public WarehouseOperationalWorkspaceStore(string storagePath, DesktopMySqlBackplaneService? backplane = null)
    {
        StoragePath = storagePath;
        _backplane = backplane;
    }

    public string StoragePath { get; }

    public static WarehouseOperationalWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new WarehouseOperationalWorkspaceStore(
            Path.Combine(root, "app_data", "warehouse-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault());
    }

    public OperationalWarehouseWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        var workspace = OperationalWarehouseWorkspace.Create(currentOperator, salesWorkspace);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<WarehouseWorkspaceSnapshot>("warehouse");
        if (backplaneSnapshot is not null)
        {
            var persistedFromMySql = backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persistedFromMySql);
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
            _backplane?.TrySaveModuleSnapshot("warehouse", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog));
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
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<WarehouseWorkspaceSnapshot>("warehouse");
        if (backplaneSnapshot is not null)
        {
            return backplaneSnapshot.ToWorkspace(currentOperator, catalogItems, warehouses);
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
        if (_backplane?.TrySaveModuleSnapshot("warehouse", snapshot, workspace.CurrentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
        {
            return;
        }

        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
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

    private sealed class WarehouseWorkspaceSnapshot
    {
        public string CurrentOperator { get; set; } = string.Empty;

        public List<OperationalWarehouseDocumentRecord> TransferOrders { get; set; } = [];

        public List<OperationalWarehouseDocumentRecord> InventoryCounts { get; set; } = [];

        public List<OperationalWarehouseDocumentRecord> WriteOffs { get; set; } = [];

        public List<WarehouseOperationLogEntry> OperationLog { get; set; } = [];

        public static WarehouseWorkspaceSnapshot FromWorkspace(OperationalWarehouseWorkspace workspace)
        {
            return new WarehouseWorkspaceSnapshot
            {
                CurrentOperator = workspace.CurrentOperator,
                TransferOrders = workspace.TransferOrders.Select(item => item.Clone()).ToList(),
                InventoryCounts = workspace.InventoryCounts.Select(item => item.Clone()).ToList(),
                WriteOffs = workspace.WriteOffs.Select(item => item.Clone()).ToList(),
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

            foreach (var logEntry in OperationLog)
            {
                workspace.OperationLog.Add(logEntry.Clone());
            }

            return workspace;
        }
    }
}
