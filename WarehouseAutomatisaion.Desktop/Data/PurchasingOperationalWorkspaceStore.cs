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

    public PurchasingOperationalWorkspaceStore(string storagePath, DesktopMySqlBackplaneService? backplane = null)
    {
        StoragePath = storagePath;
        _backplane = backplane;
    }

    public string StoragePath { get; }

    public static PurchasingOperationalWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new PurchasingOperationalWorkspaceStore(
            Path.Combine(root, "app_data", "purchasing-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault());
    }

    public OperationalPurchasingWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        var workspace = OperationalPurchasingWorkspace.Create(currentOperator, salesWorkspace);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<PurchasingWorkspaceSnapshot>("purchasing");
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
            var snapshot = JsonSerializer.Deserialize<PurchasingWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            var persisted = snapshot.ToWorkspace(currentOperator, salesWorkspace.CatalogItems, salesWorkspace.Warehouses);
            MergeWorkspace(workspace, persisted);
            _backplane?.TrySaveModuleSnapshot("purchasing", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog));
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
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<PurchasingWorkspaceSnapshot>("purchasing");
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
            var snapshot = JsonSerializer.Deserialize<PurchasingWorkspaceSnapshot>(json, SerializerOptions);
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
        if (_backplane?.TrySaveModuleSnapshot("purchasing", snapshot, workspace.CurrentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
        {
            return;
        }

        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
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
