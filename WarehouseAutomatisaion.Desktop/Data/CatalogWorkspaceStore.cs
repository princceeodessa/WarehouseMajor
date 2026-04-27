using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class CatalogWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly DesktopMySqlBackplaneService? _backplane;

    public CatalogWorkspaceStore(string storagePath, DesktopMySqlBackplaneService? backplane = null)
    {
        StoragePath = storagePath;
        _backplane = backplane;
    }

    public string StoragePath { get; }

    public static CatalogWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new CatalogWorkspaceStore(
            Path.Combine(root, "app_data", "catalog-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault());
    }

    public CatalogWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        var workspace = CatalogWorkspace.Create(currentOperator, BuildSeed(salesWorkspace));
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<CatalogWorkspaceSnapshot>("catalog");
        if (backplaneSnapshot is not null)
        {
            workspace.ReplaceFrom(backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));
            return workspace;
        }

        if (!File.Exists(StoragePath))
        {
            return workspace;
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<CatalogWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            workspace.ReplaceFrom(snapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));
            _backplane?.TrySaveModuleSnapshot("catalog", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog));
            return workspace;
        }
        catch
        {
            return workspace;
        }
    }

    public CatalogWorkspace? TryLoadExisting(
        string currentOperator,
        IReadOnlyList<string>? currencies = null,
        IReadOnlyList<string>? warehouses = null)
    {
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneSnapshot = _backplane?.TryLoadModuleSnapshot<CatalogWorkspaceSnapshot>("catalog");
        if (backplaneSnapshot is not null)
        {
            return backplaneSnapshot.ToWorkspace(currentOperator, currencies, warehouses);
        }

        if (!File.Exists(StoragePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            var snapshot = JsonSerializer.Deserialize<CatalogWorkspaceSnapshot>(json, SerializerOptions);
            return snapshot?.ToWorkspace(currentOperator, currencies, warehouses);
        }
        catch
        {
            return null;
        }
    }

    public void Save(CatalogWorkspace workspace)
    {
        var directory = Path.GetDirectoryName(StoragePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("Storage directory is not configured.");
        }

        Directory.CreateDirectory(directory);
        var snapshot = CatalogWorkspaceSnapshot.FromWorkspace(workspace);
        if (_backplane?.TrySaveModuleSnapshot("catalog", snapshot, workspace.CurrentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true)
        {
            return;
        }

        var tempPath = $"{StoragePath}.tmp";
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        File.WriteAllText(tempPath, json, Encoding.UTF8);
        File.Move(tempPath, StoragePath, true);
    }

    private static CatalogWorkspaceSeed BuildSeed(SalesWorkspace salesWorkspace)
    {
        var importRecords = salesWorkspace.OneCImport?.Items.Records ?? Array.Empty<OneCRecordSnapshot>();
        var importByCode = importRecords
            .Where(record => !string.IsNullOrWhiteSpace(record.Code))
            .GroupBy(record => record.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var importByName = importRecords
            .Where(record => !string.IsNullOrWhiteSpace(record.Title))
            .GroupBy(record => record.Title, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var bestWarehouseByItemCode = salesWorkspace.OperationalSnapshot?.StockBalances
            .GroupBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(item => item.BaselineQuantity)
                    .ThenBy(item => item.Warehouse, StringComparer.OrdinalIgnoreCase)
                    .First().Warehouse,
                StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var items = salesWorkspace.CatalogItems
            .Select(item =>
            {
                var importRecord = TryResolveImportRecord(item, importByCode, importByName);
                var category = FirstNonEmpty(
                    GetFieldDisplay(importRecord, "ЦеноваяГруппаНоменклатуры", "ЦеноваяГруппа", "ВидНоменклатуры", "ГруппаНоменклатуры", "Группа"),
                    "Без группы");
                var supplier = GetFieldDisplay(importRecord, "ОсновнойПоставщик", "Поставщик", "Производитель");
                var warehouse = FirstNonEmpty(
                    GetFieldDisplay(importRecord, "СкладПоУмолчанию", "Склад", "СкладОсновной"),
                    bestWarehouseByItemCode.TryGetValue(item.Code, out var bestWarehouse) ? bestWarehouse : string.Empty,
                    salesWorkspace.Warehouses.FirstOrDefault() ?? string.Empty);
                var notes = BuildItemNotes(importRecord);

                return new CatalogItemRecord
                {
                    Id = CreateDeterministicGuid($"catalog-item|{item.Code}|{item.Name}"),
                    Code = item.Code,
                    Name = item.Name,
                    Unit = item.Unit,
                    Category = category,
                    Supplier = supplier,
                    DefaultWarehouse = warehouse,
                    Status = string.IsNullOrWhiteSpace(importRecord?.Status) ? "Активна" : importRecord.Status,
                    CurrencyCode = salesWorkspace.Currencies.FirstOrDefault() ?? "RUB",
                    DefaultPrice = item.DefaultPrice,
                    Notes = notes,
                    SourceLabel = importRecord is null ? "Operational MySQL / catalog" : "1С import / catalog"
                };
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var operationalPriceTypes = OperationalMySqlDesktopService.TryCreateConfigured()?.TryLoadCatalogPriceTypes()
            ?? Array.Empty<OperationalCatalogPriceTypeSeed>();
        var priceTypes = operationalPriceTypes.Count > 0
            ? MapOperationalPriceTypes(operationalPriceTypes)
            : Array.Empty<CatalogPriceTypeRecord>();
        var currencies = items
            .Select(item => item.CurrencyCode)
            .Concat(priceTypes.Select(item => item.CurrencyCode))
            .Concat(salesWorkspace.Currencies)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var warehouses = items
            .Select(item => item.DefaultWarehouse)
            .Concat(salesWorkspace.Warehouses)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CatalogWorkspaceSeed
        {
            Items = items,
            PriceTypes = priceTypes,
            Discounts = Array.Empty<CatalogDiscountRecord>(),
            PriceRegistrations = Array.Empty<CatalogPriceRegistrationRecord>(),
            Currencies = currencies,
            Warehouses = warehouses
        };
    }

    private static IReadOnlyList<CatalogPriceTypeRecord> MapOperationalPriceTypes(IEnumerable<OperationalCatalogPriceTypeSeed> priceTypes)
    {
        var list = priceTypes
            .Select(priceType => new CatalogPriceTypeRecord
            {
                Id = CreateDeterministicGuid($"catalog-price-type|{priceType.Code}|{priceType.Name}"),
                Code = priceType.Code,
                Name = priceType.Name,
                CurrencyCode = priceType.CurrencyCode,
                BasePriceTypeName = priceType.BasePriceTypeName,
                RoundingRule = priceType.UsesPsychologicalRounding ? "Психологическое" : "Без округления",
                IsDefault = priceType.Name.Contains("Рознич", StringComparison.OrdinalIgnoreCase),
                IsManualEntryOnly = priceType.IsManualEntryOnly,
                UsesPsychologicalRounding = priceType.UsesPsychologicalRounding,
                Status = priceType.IsManualEntryOnly ? "Ручной" : "Рабочий"
            })
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        if (list.Count > 0 && list.All(item => !item.IsDefault))
        {
            list[0].IsDefault = true;
        }

        return list;
    }

    private static OneCRecordSnapshot? TryResolveImportRecord(
        SalesCatalogItemOption item,
        IReadOnlyDictionary<string, OneCRecordSnapshot> importByCode,
        IReadOnlyDictionary<string, OneCRecordSnapshot> importByName)
    {
        if (!string.IsNullOrWhiteSpace(item.Code) && importByCode.TryGetValue(item.Code, out var byCode))
        {
            return byCode;
        }

        if (!string.IsNullOrWhiteSpace(item.Name) && importByName.TryGetValue(item.Name, out var byName))
        {
            return byName;
        }

        return null;
    }

    private static string BuildItemNotes(OneCRecordSnapshot? record)
    {
        if (record is null)
        {
            return string.Empty;
        }

        var noteParts = new List<string>();
        var fullName = GetFieldDisplay(record, "НаименованиеПолное", "ПолноеНаименование");
        if (!string.IsNullOrWhiteSpace(fullName) && !fullName.Equals(record.Title, StringComparison.OrdinalIgnoreCase))
        {
            noteParts.Add(fullName);
        }

        var article = GetFieldDisplay(record, "Артикул");
        if (!string.IsNullOrWhiteSpace(article))
        {
            noteParts.Add($"Артикул: {article}");
        }

        var comment = GetFieldDisplay(record, "Комментарий");
        if (!string.IsNullOrWhiteSpace(comment))
        {
            noteParts.Add(comment);
        }

        return string.Join(Environment.NewLine, noteParts);
    }

    private static string GetFieldDisplay(OneCRecordSnapshot? record, params string[] fieldNames)
    {
        if (record is null)
        {
            return string.Empty;
        }

        foreach (var fieldName in fieldNames)
        {
            var field = record.FindField(fieldName);
            var value = field?.DisplayValue ?? field?.RawValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;
    }

    private static Guid CreateDeterministicGuid(string seed)
    {
        var bytes = Encoding.UTF8.GetBytes(seed);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        Span<byte> buffer = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(buffer);
        buffer[7] = (byte)((buffer[7] & 0x0F) | 0x40);
        buffer[8] = (byte)((buffer[8] & 0x3F) | 0x80);
        return new Guid(buffer);
    }

    private static IReadOnlyList<DesktopAuditEventSeed> CreateAuditSeeds(IEnumerable<CatalogOperationLogEntry> entries)
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

    private sealed class CatalogWorkspaceSnapshot
    {
        public string CurrentOperator { get; set; } = string.Empty;

        public List<CatalogItemRecord> Items { get; set; } = [];

        public List<CatalogPriceTypeRecord> PriceTypes { get; set; } = [];

        public List<CatalogDiscountRecord> Discounts { get; set; } = [];

        public List<CatalogPriceRegistrationRecord> PriceRegistrations { get; set; } = [];

        public List<CatalogOperationLogEntry> OperationLog { get; set; } = [];

        public List<string> Currencies { get; set; } = [];

        public List<string> Warehouses { get; set; } = [];

        public static CatalogWorkspaceSnapshot FromWorkspace(CatalogWorkspace workspace)
        {
            return new CatalogWorkspaceSnapshot
            {
                CurrentOperator = workspace.CurrentOperator,
                Items = workspace.Items.Select(item => item.Clone()).ToList(),
                PriceTypes = workspace.PriceTypes.Select(item => item.Clone()).ToList(),
                Discounts = workspace.Discounts.Select(item => item.Clone()).ToList(),
                PriceRegistrations = workspace.PriceRegistrations.Select(item => item.Clone()).ToList(),
                OperationLog = workspace.OperationLog.Select(item => item.Clone()).ToList(),
                Currencies = workspace.Currencies.ToList(),
                Warehouses = workspace.Warehouses.ToList()
            };
        }

        public CatalogWorkspace ToWorkspace(
            string currentOperator,
            IReadOnlyList<string>? fallbackCurrencies,
            IReadOnlyList<string>? fallbackWarehouses)
        {
            return CatalogWorkspace.Create(
                string.IsNullOrWhiteSpace(CurrentOperator) ? currentOperator : CurrentOperator,
                new CatalogWorkspaceSeed
                {
                    Items = Items.Select(item => item.Clone()).ToArray(),
                    PriceTypes = PriceTypes.Select(item => item.Clone()).ToArray(),
                    Discounts = Discounts.Select(item => item.Clone()).ToArray(),
                    PriceRegistrations = PriceRegistrations.Select(item => item.Clone()).ToArray(),
                    OperationLog = OperationLog.Select(item => item.Clone()).ToArray(),
                    Currencies = Currencies.Count > 0 ? Currencies.ToArray() : fallbackCurrencies ?? Array.Empty<string>(),
                    Warehouses = Warehouses.Count > 0 ? Warehouses.ToArray() : fallbackWarehouses ?? Array.Empty<string>()
                });
        }
    }
}
