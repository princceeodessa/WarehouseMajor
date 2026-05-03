using System.ComponentModel;
using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class PurchasingOperationalWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };
    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;
    private DesktopModuleSnapshotMetadata? _remoteMetadata;

    public PurchasingOperationalWorkspaceStore(
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

    public static PurchasingOperationalWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new PurchasingOperationalWorkspaceStore(
            Path.Combine(root, "app_data", "purchasing-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault(),
            DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled());
    }

    public OperationalPurchasingWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        EnsureBackplaneReady(currentOperator);

        var workspace = OperationalPurchasingWorkspace.Create(currentOperator, salesWorkspace);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<PurchasingWorkspaceSnapshot>("purchasing");
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            var repaired = RepairSupplierLinks(backplaneSnapshot);
            var persistedFromMySql = backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persistedFromMySql);
            if (repaired)
            {
                TrySaveToBackplane(backplaneSnapshot, currentOperator);
            }

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
            var snapshot = JsonSerializer.Deserialize<PurchasingWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            var repaired = RepairSupplierLinks(snapshot);
            var persisted = snapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persisted);
            if (repaired)
            {
                WriteSnapshot(snapshot);
            }

            var backplane = _backplane;
            var savedToBackplane = backplane?.TrySaveModuleSnapshot("purchasing", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true;
            if (savedToBackplane && backplane is not null)
            {
                _remoteMetadata = backplane.TryLoadModuleSnapshotMetadata("purchasing");
            }
            else if (_serverModeEnabled)
            {
                throw CreateRemoteSaveException("закупок");
            }

            return workspace;
        }
        catch
        {
            return workspace;
        }
    }

    public OperationalPurchasingWorkspace? TryLoadExisting(
        string currentOperator,
        IReadOnlyList<SalesCatalogItemOption>? catalogItems = null,
        IReadOnlyList<string>? warehouses = null)
    {
        EnsureBackplaneReady(currentOperator);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<PurchasingWorkspaceSnapshot>("purchasing");
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
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
            var snapshot = JsonSerializer.Deserialize<PurchasingWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is not null)
            {
                RepairSupplierLinks(snapshot);
            }

            return snapshot?.ToWorkspace(currentOperator, catalogItems, warehouses);
        }
        catch
        {
            return null;
        }
    }

    public void Save(OperationalPurchasingWorkspace workspace)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        var snapshot = PurchasingWorkspaceSnapshot.FromWorkspace(workspace);
        RepairSupplierLinks(snapshot);
        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
        {
            return;
        }

        if (_serverModeEnabled)
        {
            throw CreateRemoteSaveException("закупок");
        }

        WriteSnapshot(snapshot);
    }

    private bool TrySaveToBackplane(PurchasingWorkspaceSnapshot snapshot, string currentOperator)
    {
        if (_backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = _backplane.TrySaveModuleSnapshot("purchasing", snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded)
        {
            _remoteMetadata = result.Metadata;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotRecord<PurchasingWorkspaceSnapshot>("purchasing");
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        RepairSupplierLinks(merged);
        var retry = _backplane.TrySaveModuleSnapshot("purchasing", merged, currentOperator, latest.Metadata, CreateAuditSeeds(merged.OperationLog));
        if (!retry.Succeeded)
        {
            throw new InvalidOperationException("Данные закупок на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        return true;
    }

    private void WriteSnapshot(PurchasingWorkspaceSnapshot snapshot)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
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
            throw new InvalidOperationException("Включен режим общей БД, но подключение к серверу недоступно. Локальная загрузка закупок отключена.");
        }

        _backplane.EnsureReady(currentOperator);
    }

    private static InvalidOperationException CreateRemoteSaveException(string moduleName)
    {
        return new InvalidOperationException($"Не удалось сохранить данные {moduleName} в серверную БД. Локальное сохранение отключено для общего режима.");
    }

    private static IReadOnlyList<DesktopAuditEventSeed> CreateAuditSeeds(IEnumerable<PurchasingOperationLogEntry> entries)
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

    private static void MergeWorkspace(OperationalPurchasingWorkspace target, OperationalPurchasingWorkspace persisted)
    {
        MergeSuppliers(target.Suppliers, persisted.Suppliers);
        MergeDocuments(target.PurchaseOrders, persisted.PurchaseOrders);
        MergeDocuments(target.SupplierInvoices, persisted.SupplierInvoices);
        MergeDocuments(target.PurchaseReceipts, persisted.PurchaseReceipts);
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

        RepairSupplierLinks(target);
    }

    private static bool RepairSupplierLinks(PurchasingWorkspaceSnapshot snapshot)
    {
        var supplierById = snapshot.Suppliers
            .Where(item => item.Id != Guid.Empty)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var supplierByName = snapshot.Suppliers
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var document in snapshot.PurchaseOrders
                     .Concat(snapshot.SupplierInvoices)
                     .Concat(snapshot.PurchaseReceipts))
        {
            changed |= RepairDocumentSupplierLink(document, supplierById, supplierByName);
        }

        return changed;
    }

    private static void RepairSupplierLinks(OperationalPurchasingWorkspace workspace)
    {
        var supplierById = workspace.Suppliers
            .Where(item => item.Id != Guid.Empty)
            .GroupBy(item => item.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var supplierByName = workspace.Suppliers
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var document in workspace.PurchaseOrders
                     .Concat(workspace.SupplierInvoices)
                     .Concat(workspace.PurchaseReceipts))
        {
            RepairDocumentSupplierLink(document, supplierById, supplierByName);
        }
    }

    private static bool RepairDocumentSupplierLink(
        OperationalPurchasingDocumentRecord document,
        IReadOnlyDictionary<Guid, OperationalPurchasingSupplierRecord> supplierById,
        IReadOnlyDictionary<string, OperationalPurchasingSupplierRecord> supplierByName)
    {
        if (document.SupplierId != Guid.Empty && supplierById.TryGetValue(document.SupplierId, out var linkedSupplier))
        {
            return FillDocumentSupplierFields(document, linkedSupplier, repairId: false);
        }

        if (string.IsNullOrWhiteSpace(document.SupplierName)
            || !supplierByName.TryGetValue(document.SupplierName.Trim(), out var supplierByDocumentName))
        {
            return false;
        }

        return FillDocumentSupplierFields(document, supplierByDocumentName, repairId: true);
    }

    private static bool FillDocumentSupplierFields(
        OperationalPurchasingDocumentRecord document,
        OperationalPurchasingSupplierRecord supplier,
        bool repairId)
    {
        var changed = false;
        if (repairId && document.SupplierId != supplier.Id)
        {
            document.SupplierId = supplier.Id;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.SupplierName) && !string.IsNullOrWhiteSpace(supplier.Name))
        {
            document.SupplierName = supplier.Name;
            changed = true;
        }

        if (string.IsNullOrWhiteSpace(document.Contract) && !string.IsNullOrWhiteSpace(supplier.Contract))
        {
            document.Contract = supplier.Contract;
            changed = true;
        }

        return changed;
    }

    private static void MergeSuppliers(
        BindingList<OperationalPurchasingSupplierRecord> target,
        IEnumerable<OperationalPurchasingSupplierRecord> persistedSuppliers)
    {
        var targetByKey = target
            .Select(item => (Key: BuildSupplierKey(item), Item: item))
            .Where(item => !string.IsNullOrWhiteSpace(item.Key))
            .ToDictionary(item => item.Key, item => item.Item, StringComparer.OrdinalIgnoreCase);

        foreach (var persisted in persistedSuppliers)
        {
            var key = BuildSupplierKey(persisted);
            if (string.IsNullOrWhiteSpace(key))
            {
                target.Add(persisted.Clone());
                continue;
            }

            if (targetByKey.TryGetValue(key, out var current))
            {
                current.CopyFrom(MergeSupplier(current, persisted));
                continue;
            }

            var clone = persisted.Clone();
            target.Add(clone);
            targetByKey[key] = clone;
        }
    }

    private static void MergeDocuments(
        BindingList<OperationalPurchasingDocumentRecord> target,
        IEnumerable<OperationalPurchasingDocumentRecord> persistedDocuments)
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
        BindingList<PurchasingOperationLogEntry> target,
        IEnumerable<PurchasingOperationLogEntry> persistedEntries)
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

    private static OperationalPurchasingSupplierRecord MergeSupplier(
        OperationalPurchasingSupplierRecord runtime,
        OperationalPurchasingSupplierRecord persisted)
    {
        var merged = runtime.Clone();
        merged.Id = persisted.Id != Guid.Empty ? persisted.Id : runtime.Id;
        merged.Name = FirstNonEmpty(persisted.Name, runtime.Name);
        merged.Code = FirstNonEmpty(persisted.Code, runtime.Code);
        merged.Status = FirstNonEmpty(persisted.Status, runtime.Status);
        merged.TaxId = FirstNonEmpty(persisted.TaxId, runtime.TaxId);
        merged.Phone = FirstNonEmpty(persisted.Phone, runtime.Phone);
        merged.Email = FirstNonEmpty(persisted.Email, runtime.Email);
        merged.Contract = FirstNonEmpty(persisted.Contract, runtime.Contract);
        merged.SourceLabel = MergeSourceLabels(runtime.SourceLabel, persisted.SourceLabel);
        merged.Fields = ChoosePreferredFields(runtime.Fields, persisted.Fields, persisted.SourceLabel);
        return merged;
    }

    private static OperationalPurchasingDocumentRecord MergeDocument(
        OperationalPurchasingDocumentRecord runtime,
        OperationalPurchasingDocumentRecord persisted)
    {
        var merged = runtime.Clone();
        merged.Id = persisted.Id != Guid.Empty ? persisted.Id : runtime.Id;
        merged.DocumentType = FirstNonEmpty(persisted.DocumentType, runtime.DocumentType);
        merged.Number = FirstNonEmpty(persisted.Number, runtime.Number);
        merged.DocumentDate = persisted.DocumentDate != default ? persisted.DocumentDate : runtime.DocumentDate;
        merged.DueDate = persisted.DueDate ?? runtime.DueDate;
        merged.SupplierId = persisted.SupplierId != Guid.Empty ? persisted.SupplierId : runtime.SupplierId;
        merged.SupplierName = FirstNonEmpty(persisted.SupplierName, runtime.SupplierName);
        merged.Status = FirstNonEmpty(persisted.Status, runtime.Status);
        merged.Contract = FirstNonEmpty(persisted.Contract, runtime.Contract);
        merged.Warehouse = FirstNonEmpty(persisted.Warehouse, runtime.Warehouse);
        merged.RelatedOrderId = persisted.RelatedOrderId != Guid.Empty ? persisted.RelatedOrderId : runtime.RelatedOrderId;
        merged.RelatedOrderNumber = FirstNonEmpty(persisted.RelatedOrderNumber, runtime.RelatedOrderNumber);
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

    private static BindingList<OperationalPurchasingLineRecord> ChoosePreferredLines(
        BindingList<OperationalPurchasingLineRecord> runtimeLines,
        BindingList<OperationalPurchasingLineRecord> persistedLines,
        string persistedSourceLabel)
    {
        var source = persistedLines.Count > runtimeLines.Count || IsLocalSource(persistedSourceLabel)
            ? persistedLines
            : runtimeLines;
        return new BindingList<OperationalPurchasingLineRecord>(source.Select(item => item.Clone()).ToList());
    }

    private static string BuildSupplierKey(OperationalPurchasingSupplierRecord supplier)
    {
        return !string.IsNullOrWhiteSpace(supplier.Code)
            ? $"code:{supplier.Code}"
            : $"name:{supplier.Name}";
    }

    private static string BuildDocumentKey(OperationalPurchasingDocumentRecord document)
    {
        return !string.IsNullOrWhiteSpace(document.Number)
            ? $"{document.DocumentType}|{document.Number}"
            : $"{document.DocumentType}|{document.SupplierName}|{document.DocumentDate:yyyyMMdd}";
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

    private static PurchasingWorkspaceSnapshot MergeSnapshots(PurchasingWorkspaceSnapshot server, PurchasingWorkspaceSnapshot local)
    {
        return new PurchasingWorkspaceSnapshot
        {
            CurrentOperator = string.IsNullOrWhiteSpace(local.CurrentOperator) ? server.CurrentOperator : local.CurrentOperator,
            Suppliers = MergeRecords(server.Suppliers, local.Suppliers, BuildSupplierKey, item => item.Clone()),
            PurchaseOrders = MergeRecords(server.PurchaseOrders, local.PurchaseOrders, BuildDocumentKey, item => item.Clone()),
            SupplierInvoices = MergeRecords(server.SupplierInvoices, local.SupplierInvoices, BuildDocumentKey, item => item.Clone()),
            PurchaseReceipts = MergeRecords(server.PurchaseReceipts, local.PurchaseReceipts, BuildDocumentKey, item => item.Clone()),
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

    private sealed class PurchasingWorkspaceSnapshot
    {
        public string CurrentOperator { get; set; } = string.Empty;

        public List<OperationalPurchasingSupplierRecord> Suppliers { get; set; } = [];

        public List<OperationalPurchasingDocumentRecord> PurchaseOrders { get; set; } = [];

        public List<OperationalPurchasingDocumentRecord> SupplierInvoices { get; set; } = [];

        public List<OperationalPurchasingDocumentRecord> PurchaseReceipts { get; set; } = [];

        public List<PurchasingOperationLogEntry> OperationLog { get; set; } = [];

        public static PurchasingWorkspaceSnapshot FromWorkspace(OperationalPurchasingWorkspace workspace)
        {
            return new PurchasingWorkspaceSnapshot
            {
                CurrentOperator = workspace.CurrentOperator,
                Suppliers = workspace.Suppliers.Select(item => item.Clone()).ToList(),
                PurchaseOrders = workspace.PurchaseOrders.Select(item => item.Clone()).ToList(),
                SupplierInvoices = workspace.SupplierInvoices.Select(item => item.Clone()).ToList(),
                PurchaseReceipts = workspace.PurchaseReceipts.Select(item => item.Clone()).ToList(),
                OperationLog = workspace.OperationLog.Select(item => item.Clone()).ToList()
            };
        }

        public OperationalPurchasingWorkspace ToWorkspace(
            string currentOperator,
            IReadOnlyList<SalesCatalogItemOption>? catalogItems,
            IReadOnlyList<string>? warehouses)
        {
            var workspace = OperationalPurchasingWorkspace.CreateEmpty(
                string.IsNullOrWhiteSpace(CurrentOperator) ? currentOperator : CurrentOperator,
                catalogItems,
                warehouses);

            foreach (var supplier in Suppliers)
            {
                workspace.Suppliers.Add(supplier.Clone());
            }

            foreach (var order in PurchaseOrders)
            {
                workspace.PurchaseOrders.Add(order.Clone());
            }

            foreach (var invoice in SupplierInvoices)
            {
                workspace.SupplierInvoices.Add(invoice.Clone());
            }

            foreach (var receipt in PurchaseReceipts)
            {
                workspace.PurchaseReceipts.Add(receipt.Clone());
            }

            foreach (var logEntry in OperationLog)
            {
                workspace.OperationLog.Add(logEntry.Clone());
            }

            return workspace;
        }
    }
}
