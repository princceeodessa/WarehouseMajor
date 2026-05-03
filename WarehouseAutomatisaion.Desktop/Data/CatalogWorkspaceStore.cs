using System.Text;
using System.Text.Json;
using WarehouseAutomatisaion.Desktop.Text;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class CatalogWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true
    };

    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;
    private DesktopModuleSnapshotMetadata? _remoteMetadata;

    public CatalogWorkspaceStore(
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

    public static CatalogWorkspaceStore CreateDefault()
    {
        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new CatalogWorkspaceStore(
            Path.Combine(root, "app_data", "catalog-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault(),
            DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled());
    }

    public CatalogWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        EnsureBackplaneReady(currentOperator);

        var seed = BuildSeed(salesWorkspace);
        var workspace = CatalogWorkspace.Create(currentOperator, seed);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<CatalogWorkspaceSnapshot>("catalog");
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            var normalized = NormalizeSnapshot(backplaneSnapshot);
            normalized |= ReconcileSnapshot(backplaneSnapshot, seed);
            workspace.ReplaceFrom(backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));
            if (normalized)
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
            var snapshot = JsonSerializer.Deserialize<CatalogWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is null)
            {
                return workspace;
            }

            var normalized = NormalizeSnapshot(snapshot);
            normalized |= ReconcileSnapshot(snapshot, seed);
            workspace.ReplaceFrom(snapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));
            if (normalized)
            {
                WriteSnapshot(snapshot);
            }

            var backplane = _backplane;
            var savedToBackplane = backplane?.TrySaveModuleSnapshot("catalog", snapshot, currentOperator, CreateAuditSeeds(snapshot.OperationLog)) == true;
            if (savedToBackplane && backplane is not null)
            {
                _remoteMetadata = backplane.TryLoadModuleSnapshotMetadata("catalog");
            }
            else if (_serverModeEnabled)
            {
                throw CreateRemoteSaveException("товаров");
            }

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
        EnsureBackplaneReady(currentOperator);
        _backplane?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = _backplane?.TryLoadModuleSnapshotRecord<CatalogWorkspaceSnapshot>("catalog");
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            NormalizeSnapshot(backplaneSnapshot);
            return backplaneSnapshot.ToWorkspace(currentOperator, currencies, warehouses);
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
            var snapshot = JsonSerializer.Deserialize<CatalogWorkspaceSnapshot>(json, SerializerOptions);
            if (snapshot is not null)
            {
                NormalizeSnapshot(snapshot);
            }

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
        NormalizeSnapshot(snapshot);
        ReconcileSnapshot(snapshot, new CatalogWorkspaceSeed());
        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
        {
            return;
        }

        if (_serverModeEnabled)
        {
            throw CreateRemoteSaveException("товаров");
        }

        WriteSnapshot(snapshot);
    }

    private bool TrySaveToBackplane(CatalogWorkspaceSnapshot snapshot, string currentOperator)
    {
        if (_backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = _backplane.TrySaveModuleSnapshot("catalog", snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded)
        {
            _remoteMetadata = result.Metadata;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = _backplane.TryLoadModuleSnapshotRecord<CatalogWorkspaceSnapshot>("catalog");
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        NormalizeSnapshot(merged);
        var retry = _backplane.TrySaveModuleSnapshot("catalog", merged, currentOperator, latest.Metadata, CreateAuditSeeds(merged.OperationLog));
        if (!retry.Succeeded)
        {
            throw new InvalidOperationException("Данные товаров на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        return true;
    }

    private void WriteSnapshot(CatalogWorkspaceSnapshot snapshot)
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
            throw new InvalidOperationException("Включен режим общей БД, но подключение к серверу недоступно. Локальная загрузка товаров отключена.");
        }

        _backplane.EnsureReady(currentOperator);
    }

    private static InvalidOperationException CreateRemoteSaveException(string moduleName)
    {
        return new InvalidOperationException($"Не удалось сохранить данные {moduleName} в серверную БД. Локальное сохранение отключено для общего режима.");
    }

    private static bool NormalizeSnapshot(CatalogWorkspaceSnapshot snapshot)
    {
        var changed = false;

        snapshot.CurrentOperator = Normalize(snapshot.CurrentOperator, ref changed);
        NormalizeList(snapshot.Currencies, ref changed);
        NormalizeList(snapshot.Warehouses, ref changed);

        foreach (var item in snapshot.Items)
        {
            item.Code = Normalize(item.Code, ref changed);
            item.Name = Normalize(item.Name, ref changed);
            item.Unit = Normalize(item.Unit, ref changed);
            item.Category = Normalize(item.Category, ref changed);
            item.Supplier = Normalize(item.Supplier, ref changed);
            item.DefaultWarehouse = Normalize(item.DefaultWarehouse, ref changed);
            item.Status = Normalize(item.Status, ref changed);
            item.CurrencyCode = Normalize(item.CurrencyCode, ref changed);
            item.BarcodeValue = Normalize(item.BarcodeValue, ref changed);
            item.BarcodeFormat = Normalize(item.BarcodeFormat, ref changed);
            item.QrPayload = Normalize(item.QrPayload, ref changed);
            item.Notes = Normalize(item.Notes, ref changed);
            item.SourceLabel = Normalize(item.SourceLabel, ref changed);
        }

        foreach (var priceType in snapshot.PriceTypes)
        {
            priceType.Code = Normalize(priceType.Code, ref changed);
            priceType.Name = Normalize(priceType.Name, ref changed);
            priceType.CurrencyCode = Normalize(priceType.CurrencyCode, ref changed);
            priceType.BasePriceTypeName = Normalize(priceType.BasePriceTypeName, ref changed);
            priceType.RoundingRule = Normalize(priceType.RoundingRule, ref changed);
            priceType.Status = Normalize(priceType.Status, ref changed);
        }

        foreach (var discount in snapshot.Discounts)
        {
            discount.Name = Normalize(discount.Name, ref changed);
            discount.PriceTypeName = Normalize(discount.PriceTypeName, ref changed);
            discount.Period = Normalize(discount.Period, ref changed);
            discount.Scope = Normalize(discount.Scope, ref changed);
            discount.Status = Normalize(discount.Status, ref changed);
            discount.Comment = Normalize(discount.Comment, ref changed);
        }

        foreach (var document in snapshot.PriceRegistrations)
        {
            document.Number = Normalize(document.Number, ref changed);
            document.PriceTypeName = Normalize(document.PriceTypeName, ref changed);
            document.CurrencyCode = Normalize(document.CurrencyCode, ref changed);
            document.Status = Normalize(document.Status, ref changed);
            document.Comment = Normalize(document.Comment, ref changed);

            foreach (var line in document.Lines)
            {
                line.ItemCode = Normalize(line.ItemCode, ref changed);
                line.ItemName = Normalize(line.ItemName, ref changed);
                line.Unit = Normalize(line.Unit, ref changed);
            }
        }

        foreach (var logEntry in snapshot.OperationLog)
        {
            logEntry.Actor = Normalize(logEntry.Actor, ref changed);
            logEntry.EntityType = Normalize(logEntry.EntityType, ref changed);
            logEntry.EntityNumber = Normalize(logEntry.EntityNumber, ref changed);
            logEntry.Action = Normalize(logEntry.Action, ref changed);
            logEntry.Result = Normalize(logEntry.Result, ref changed);
            logEntry.Message = Normalize(logEntry.Message, ref changed);
        }

        return changed;
    }

    private static bool ReconcileSnapshot(CatalogWorkspaceSnapshot snapshot, CatalogWorkspaceSeed seed)
    {
        var changed = false;
        changed |= AddMissingSeedItems(snapshot, seed);
        changed |= DeduplicateItems(snapshot);
        changed |= MergeLookupValues(snapshot.Currencies, seed.Currencies);
        changed |= MergeLookupValues(snapshot.Warehouses, seed.Warehouses);
        return changed;
    }

    private static bool AddMissingSeedItems(CatalogWorkspaceSnapshot snapshot, CatalogWorkspaceSeed seed)
    {
        if (seed.Items.Count == 0)
        {
            return false;
        }

        var existingCodes = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .Select(item => item.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var existingNames = snapshot.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        foreach (var item in seed.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.Code))
            {
                if (existingCodes.Contains(item.Code))
                {
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(item.Name) && existingNames.Contains(item.Name))
            {
                continue;
            }

            var clone = item.Clone();
            clone.Id = clone.Id == Guid.Empty
                ? CreateDeterministicGuid($"catalog-item|{clone.Code}|{clone.Name}")
                : clone.Id;
            clone.Category = FirstNonEmpty(clone.Category, "Без группы");
            clone.Status = FirstNonEmpty(clone.Status, "Активна");
            clone.CurrencyCode = FirstNonEmpty(clone.CurrencyCode, "RUB");
            clone.Unit = FirstNonEmpty(clone.Unit, "шт");
            clone.SourceLabel = FirstNonEmpty(clone.SourceLabel, "Документы продаж");
            snapshot.Items.Add(clone);

            if (!string.IsNullOrWhiteSpace(clone.Code))
            {
                existingCodes.Add(clone.Code);
            }

            if (!string.IsNullOrWhiteSpace(clone.Name))
            {
                existingNames.Add(clone.Name);
            }

            changed = true;
        }

        return changed;
    }

    private static bool DeduplicateItems(CatalogWorkspaceSnapshot snapshot)
    {
        var merged = new List<CatalogItemRecord>();
        var changed = false;

        foreach (var group in snapshot.Items.GroupBy(item => BuildCatalogItemDedupeKey(item), StringComparer.OrdinalIgnoreCase))
        {
            var items = group.ToArray();
            if (string.IsNullOrWhiteSpace(group.Key) || items.Length == 1)
            {
                merged.AddRange(items.Select(item => item.Clone()));
                continue;
            }

            merged.Add(MergeCatalogItems(items));
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        snapshot.Items = merged
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return true;
    }

    private static CatalogItemRecord MergeCatalogItems(IReadOnlyList<CatalogItemRecord> items)
    {
        var primary = items
            .OrderByDescending(GetCatalogItemCompletenessScore)
            .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .First()
            .Clone();

        foreach (var item in items)
        {
            primary.Name = FirstNonEmpty(primary.Name, item.Name);
            primary.Unit = FirstNonEmpty(primary.Unit, item.Unit);
            primary.Category = FirstNonEmpty(primary.Category, item.Category);
            primary.Supplier = FirstNonEmpty(primary.Supplier, item.Supplier);
            primary.DefaultWarehouse = FirstNonEmpty(primary.DefaultWarehouse, item.DefaultWarehouse);
            primary.Status = FirstNonEmpty(primary.Status, item.Status);
            primary.CurrencyCode = FirstNonEmpty(primary.CurrencyCode, item.CurrencyCode, "RUB");
            primary.BarcodeValue = FirstNonEmpty(primary.BarcodeValue, item.BarcodeValue);
            primary.BarcodeFormat = FirstNonEmpty(primary.BarcodeFormat, item.BarcodeFormat, "Code128");
            primary.QrPayload = FirstNonEmpty(primary.QrPayload, item.QrPayload);
            primary.Notes = FirstNonEmpty(primary.Notes, item.Notes);
            primary.SourceLabel = MergeSourceLabel(primary.SourceLabel, item.SourceLabel);
            if (primary.DefaultPrice <= 0m && item.DefaultPrice > 0m)
            {
                primary.DefaultPrice = item.DefaultPrice;
            }
        }

        return primary;
    }

    private static int GetCatalogItemCompletenessScore(CatalogItemRecord item)
    {
        var score = 0;
        score += string.IsNullOrWhiteSpace(item.Name) ? 0 : 4;
        score += string.IsNullOrWhiteSpace(item.Category) ? 0 : 2;
        score += string.IsNullOrWhiteSpace(item.Supplier) ? 0 : 2;
        score += string.IsNullOrWhiteSpace(item.DefaultWarehouse) ? 0 : 2;
        score += string.IsNullOrWhiteSpace(item.BarcodeValue) ? 0 : 2;
        score += item.DefaultPrice > 0m ? 2 : 0;
        score += string.IsNullOrWhiteSpace(item.Notes) ? 0 : 1;
        return score;
    }

    private static string BuildCatalogItemDedupeKey(CatalogItemRecord item)
    {
        return !string.IsNullOrWhiteSpace(item.Code)
            ? $"code:{item.Code.Trim()}"
            : !string.IsNullOrWhiteSpace(item.Name)
                ? $"name:{item.Name.Trim()}"
                : string.Empty;
    }

    private static bool MergeLookupValues(ICollection<string> target, IEnumerable<string> source)
    {
        var changed = false;
        var existing = target
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var value in source.Where(value => !string.IsNullOrWhiteSpace(value)))
        {
            if (existing.Add(value))
            {
                target.Add(value);
                changed = true;
            }
        }

        return changed;
    }

    private static string MergeSourceLabel(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(current))
        {
            return next;
        }

        if (string.IsNullOrWhiteSpace(next) || current.Contains(next, StringComparison.OrdinalIgnoreCase))
        {
            return current;
        }

        return $"{current} + {next}";
    }

    private static void NormalizeList(IList<string> values, ref bool changed)
    {
        for (var i = 0; i < values.Count; i++)
        {
            values[i] = Normalize(values[i], ref changed);
        }
    }

    private static string Normalize(string value, ref bool changed)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        if (!string.Equals(value, normalized, StringComparison.Ordinal))
        {
            changed = true;
        }

        return normalized;
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

    private static CatalogWorkspaceSnapshot MergeSnapshots(CatalogWorkspaceSnapshot server, CatalogWorkspaceSnapshot local)
    {
        return new CatalogWorkspaceSnapshot
        {
            CurrentOperator = string.IsNullOrWhiteSpace(local.CurrentOperator) ? server.CurrentOperator : local.CurrentOperator,
            Items = MergeRecords(server.Items, local.Items, BuildItemKey, item => item.Clone()),
            PriceTypes = MergeRecords(server.PriceTypes, local.PriceTypes, BuildPriceTypeKey, item => item.Clone()),
            Discounts = MergeRecords(server.Discounts, local.Discounts, BuildDiscountKey, item => item.Clone()),
            PriceRegistrations = MergeRecords(server.PriceRegistrations, local.PriceRegistrations, BuildPriceRegistrationKey, item => item.Clone()),
            OperationLog = server.OperationLog
                .Concat(local.OperationLog)
                .GroupBy(item => item.Id == Guid.Empty ? $"{item.EntityType}|{item.EntityNumber}|{item.Action}|{item.LoggedAt:O}" : item.Id.ToString("N"), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderByDescending(item => item.LoggedAt).First().Clone())
                .OrderByDescending(item => item.LoggedAt)
                .Take(500)
                .ToList(),
            Currencies = server.Currencies
                .Concat(local.Currencies)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            Warehouses = server.Warehouses
                .Concat(local.Warehouses)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
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

    private static string BuildItemKey(CatalogItemRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : !string.IsNullOrWhiteSpace(item.Code)
                ? $"code:{item.Code}"
                : $"name:{item.Name}";
    }

    private static string BuildPriceTypeKey(CatalogPriceTypeRecord item)
    {
        return item.Id != Guid.Empty
            ? $"id:{item.Id:N}"
            : !string.IsNullOrWhiteSpace(item.Code)
                ? $"code:{item.Code}"
                : $"name:{item.Name}";
    }

    private static string BuildDiscountKey(CatalogDiscountRecord item)
    {
        return item.Id != Guid.Empty ? $"id:{item.Id:N}" : $"name:{item.Name}|{item.PriceTypeName}|{item.Period}";
    }

    private static string BuildPriceRegistrationKey(CatalogPriceRegistrationRecord item)
    {
        return item.Id != Guid.Empty ? $"id:{item.Id:N}" : $"number:{item.Number}";
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
