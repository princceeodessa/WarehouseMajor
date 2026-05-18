using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using WarehouseAutomatisaion.Desktop.Text;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed class CatalogWorkspaceStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false
    };

    private DesktopMySqlBackplaneService? _backplane;
    private readonly bool _serverModeEnabled;
    private DesktopModuleSnapshotMetadata? _remoteMetadata;
    private string _lastSavedSnapshotHash = string.Empty;
    private bool _hasPendingLocalSync;
    private DateTime _lastSyncedLocalSnapshotWriteUtc = DateTime.MinValue;

    // Release 1.0.129: in-memory cache на уровне процесса (НЕ файл-снимок).
    // БД — единственный источник истины. Кэш — fast-path для повторных открытий
    // карточки товара / Каталога. Любой Save() сбрасывает кэш через InvalidateCache,
    // следующий Load перечитает app_catalog_items напрямую.
    private static CatalogWorkspace? s_cachedWorkspace;
    private static SalesWorkspace? s_cachedSalesWorkspace;
    private static readonly object s_cacheLock = new();

    public static void InvalidateCache()
    {
        lock (s_cacheLock)
        {
            s_cachedWorkspace = null;
            s_cachedSalesWorkspace = null;
        }
    }

    /// <summary>
    /// Release 1.0.129: lazy-load цен по конкретному товару из Backplane.
    /// На initial load каталога мы больше не тянем 70 591 строку app_catalog_item_prices —
    /// карточка товара вызывает этот метод и получает ~20 строк по item_id за один SELECT.
    /// </summary>
    public IReadOnlyList<CatalogItemPriceRecord> LoadPricesForItem(Guid itemId)
    {
        return TryGetBackplane()?.LoadPricesForItem(itemId) ?? Array.Empty<CatalogItemPriceRecord>();
    }

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

    public bool IsServerModeEnabled => _serverModeEnabled;

    public bool HasPendingLocalSync
    {
        get
        {
            if (!_serverModeEnabled)
            {
                return false;
            }

            _hasPendingLocalSync = ShouldPromoteLocalSnapshot(_remoteMetadata);
            return _hasPendingLocalSync;
        }
    }

    public static CatalogWorkspaceStore CreateDefault()
    {
        // Release 1.0.106: серверная БД — единственный поддерживаемый источник данных.
        // Если RemoteDatabase отключён в appsettings — падаем здесь же с понятной ошибкой,
        // чтобы случайно не оказаться в legacy local-mode, который пишет JSON на диск.
        // (ValidateInfrastructure ловит то же на старте, но эта проверка защищает от
        // прямых вызовов CreateDefault из тестов / скриптов / будущего кода.)
        if (!DesktopRemoteDatabaseSettings.IsRemoteDatabaseEnabled())
        {
            throw new InvalidOperationException(
                "CatalogWorkspaceStore требует включённой серверной БД (RemoteDatabase.Enabled=true в appsettings).");
        }

        var root = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new CatalogWorkspaceStore(
            Path.Combine(root, "app_data", "catalog-workspace.json"),
            DesktopMySqlBackplaneService.TryCreateDefault(),
            serverModeEnabled: true);
    }

    public CatalogWorkspace LoadOrCreate(string currentOperator, SalesWorkspace salesWorkspace)
    {
        // Release 1.0.129: fast-path. Если в этой сессии уже грузили каталог
        // и тот же sales workspace — возвращаем тот же инстанс. Save() инвалидирует
        // кэш, так что после правки следующий открыватель прочтёт свежие данные из БД.
        lock (s_cacheLock)
        {
            if (s_cachedWorkspace != null && ReferenceEquals(s_cachedSalesWorkspace, salesWorkspace))
            {
                return s_cachedWorkspace;
            }
        }

        var result = LoadOrCreateInternal(currentOperator, salesWorkspace);

        lock (s_cacheLock)
        {
            s_cachedWorkspace = result;
            s_cachedSalesWorkspace = salesWorkspace;
        }
        return result;
    }

    private CatalogWorkspace LoadOrCreateInternal(string currentOperator, SalesWorkspace salesWorkspace)
    {
        EnsureBackplaneReady(currentOperator);
        TryGetBackplane()?.TryEnsureUserProfile(currentOperator);

        // Серверная БД — единственный источник истины. Локальные JSON-кэши и
        // operational-схема не используются для каталога: всё читаем напрямую из
        // app_catalog_items / app_catalog_price_registrations. Никакого reconcile
        // и автосохранения «слиянием» — иначе seed с нулевыми ценами затирает
        // импортированные данные.
        var workspace = CatalogWorkspace.CreateEmpty(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses);

        if (_serverModeEnabled)
        {
            var backplaneRecord = TryGetBackplane()?.TryLoadCatalogWorkspaceSnapshotRecord();
            if (backplaneRecord is not null)
            {
                var backplaneSnapshot = backplaneRecord.Snapshot;
                _remoteMetadata = backplaneRecord.Metadata;
                _hasPendingLocalSync = false;
                NormalizeSnapshot(backplaneSnapshot);
                workspace.ReplaceFrom(backplaneSnapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));

                // Release 1.0.107: после ReplaceFrom WPF получает событие Changed и
                // ProductsWorkspaceView через дебаунс вызывает Save(). Раньше мы записывали
                // в _lastSavedSnapshotHash хеш МЕТАДАННЫХ из Backplane (PayloadHash) —
                // он не совпадает с локально-посчитанным хешем (другой алгоритм
                // сериализации, плюс импорт из CSV ставил «imported-from-1c-1» как hash).
                // В итоге IsSnapshotAlreadySaved всегда возвращал false → каждый старт
                // вкладки «Товары» провоцировал полную перезапись 8898 строк по сети →
                // зависание + ошибка save. Теперь зафиксируем «честный» локальный хеш,
                // чтобы первый автосейв был no-op.
                var loadedSnapshot = CatalogWorkspaceSnapshot.FromWorkspace(workspace);
                NormalizeSnapshot(loadedSnapshot);
                ReconcileSnapshot(loadedSnapshot, new CatalogWorkspaceSeed());
                _lastSavedSnapshotHash = ComputeSnapshotHash(loadedSnapshot);
            }
            return workspace;
        }

        // Локальный режим (Backplane выключен) — fallback на JSON-файл.
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

            NormalizeSnapshot(snapshot);
            workspace.ReplaceFrom(snapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses));
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
        TryGetBackplane()?.TryEnsureUserProfile(currentOperator);

        var backplaneRecord = TryGetBackplane()?.TryLoadCatalogWorkspaceSnapshotRecord();
        if (backplaneRecord is not null)
        {
            var backplaneSnapshot = backplaneRecord.Snapshot;
            _remoteMetadata = backplaneRecord.Metadata;
            _lastSavedSnapshotHash = backplaneRecord.Metadata.PayloadHash;
            NormalizeSnapshot(backplaneSnapshot);
            return backplaneSnapshot.ToWorkspace(currentOperator, currencies, warehouses);
        }

        var legacyBackplaneRecord = TryGetBackplane()?.TryLoadModuleSnapshotRecord<CatalogWorkspaceSnapshot>("catalog");
        if (legacyBackplaneRecord is not null)
        {
            var backplaneSnapshot = legacyBackplaneRecord.Snapshot;
            NormalizeSnapshot(backplaneSnapshot);
            if (TrySaveToBackplane(backplaneSnapshot, currentOperator))
            {
                _lastSavedSnapshotHash = ComputeSnapshotHash(backplaneSnapshot);
            }

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
        var snapshotHash = ComputeSnapshotHash(snapshot);
        if (IsSnapshotAlreadySaved(snapshotHash))
        {
            return;
        }

        if (TrySaveToBackplane(snapshot, workspace.CurrentOperator))
        {
            _lastSavedSnapshotHash = snapshotHash;
            // Release 1.0.129: после реального сохранения сбрасываем in-memory cache.
            // Это гарантирует «БД — приоритет»: следующий LoadOrCreate перечитает
            // app_catalog_items напрямую, а не отдаст устаревший cached workspace.
            InvalidateCache();
            return;
        }

        throw CreateRemoteSaveException("каталога");
    }

    public CatalogWorkspace? TrySyncPendingLocalSnapshot(string currentOperator, SalesWorkspace salesWorkspace)
    {
        if (!_serverModeEnabled)
        {
            return null;
        }

        var seed = BuildSeed(salesWorkspace);
        var latestMetadata = TryGetBackplane()?.TryLoadCatalogWorkspaceSnapshotMetadata();
        if (latestMetadata is not null)
        {
            _remoteMetadata = latestMetadata;
        }

        if (!TryPromoteLocalSnapshotIfNewer(_remoteMetadata, seed, currentOperator, out var promotedSnapshot)
            || promotedSnapshot is null)
        {
            return null;
        }

        return promotedSnapshot.ToWorkspace(currentOperator, salesWorkspace.Currencies, salesWorkspace.Warehouses);
    }

    private bool TryPromoteLocalSnapshotIfNewer(
        DesktopModuleSnapshotMetadata? remoteMetadata,
        CatalogWorkspaceSeed seed,
        string currentOperator,
        out CatalogWorkspaceSnapshot? promotedSnapshot)
    {
        promotedSnapshot = null;
        if (!_serverModeEnabled || !ShouldPromoteLocalSnapshot(remoteMetadata))
        {
            return false;
        }

        var localUpdatedAtUtc = File.GetLastWriteTimeUtc(StoragePath);
        if (!TryReadLocalSnapshot(out var localSnapshot) || localSnapshot is null)
        {
            return false;
        }

        NormalizeSnapshot(localSnapshot);
        ReconcileSnapshot(localSnapshot, seed);
        var snapshotHash = ComputeSnapshotHash(localSnapshot);
        if (string.Equals(snapshotHash, remoteMetadata?.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            _lastSavedSnapshotHash = snapshotHash;
            MarkLocalSnapshotSynced(localUpdatedAtUtc);
            return false;
        }

        if (!TrySaveToBackplane(localSnapshot, currentOperator))
        {
            return false;
        }

        promotedSnapshot = localSnapshot;
        _lastSavedSnapshotHash = snapshotHash;
        MarkLocalSnapshotSynced(localUpdatedAtUtc);
        return true;
    }

    private bool ShouldPromoteLocalSnapshot(DesktopModuleSnapshotMetadata? remoteMetadata)
    {
        if (!File.Exists(StoragePath))
        {
            return false;
        }

        if (remoteMetadata is null)
        {
            return true;
        }

        var localUpdatedAtUtc = File.GetLastWriteTimeUtc(StoragePath);
        if (localUpdatedAtUtc <= _lastSyncedLocalSnapshotWriteUtc.AddMilliseconds(1))
        {
            return false;
        }

        return localUpdatedAtUtc > remoteMetadata.UpdatedAtUtc.AddSeconds(1);
    }

    private void MarkLocalSnapshotSynced(DateTime localUpdatedAtUtc)
    {
        if (localUpdatedAtUtc > _lastSyncedLocalSnapshotWriteUtc)
        {
            _lastSyncedLocalSnapshotWriteUtc = localUpdatedAtUtc;
        }

        _hasPendingLocalSync = false;
    }

    private bool TryReadLocalSnapshot(out CatalogWorkspaceSnapshot? snapshot)
    {
        snapshot = null;
        try
        {
            if (!File.Exists(StoragePath))
            {
                return false;
            }

            var json = File.ReadAllText(StoragePath, Encoding.UTF8);
            snapshot = JsonSerializer.Deserialize<CatalogWorkspaceSnapshot>(json, SerializerOptions);
            return snapshot is not null;
        }
        catch
        {
            return false;
        }
    }

    private bool TrySaveToBackplane(CatalogWorkspaceSnapshot snapshot, string currentOperator)
    {
        var snapshotHash = ComputeSnapshotHash(snapshot);
        if (string.Equals(snapshotHash, _remoteMetadata?.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            _lastSavedSnapshotHash = snapshotHash;
            return true;
        }

        var backplane = TryGetBackplane();
        if (backplane is null)
        {
            return false;
        }

        var auditEvents = CreateAuditSeeds(snapshot.OperationLog);
        var result = backplane.TrySaveCatalogWorkspaceSnapshot(snapshot, currentOperator, _remoteMetadata, auditEvents);
        if (result.Succeeded && result.Metadata is not null)
        {
            _remoteMetadata = result.Metadata;
            _lastSavedSnapshotHash = result.Metadata.PayloadHash;
            return true;
        }

        if (result.State != DesktopModuleSnapshotSaveState.Conflict)
        {
            return false;
        }

        var latest = backplane.TryLoadCatalogWorkspaceSnapshotRecord();
        if (latest is null)
        {
            return false;
        }

        var merged = MergeSnapshots(latest.Snapshot, snapshot);
        NormalizeSnapshot(merged);
        var retry = backplane.TrySaveCatalogWorkspaceSnapshot(merged, currentOperator, latest.Metadata, CreateAuditSeeds(merged.OperationLog));
        if (!retry.Succeeded || retry.Metadata is null)
        {
            throw new InvalidOperationException("Данные товаров на сервере изменились другим рабочим местом. Обновите данные и повторите действие.");
        }

        _remoteMetadata = retry.Metadata;
        _lastSavedSnapshotHash = retry.Metadata.PayloadHash;
        return true;
    }

    private bool IsSnapshotAlreadySaved(string snapshotHash)
    {
        return !string.IsNullOrWhiteSpace(snapshotHash)
               && !HasPendingLocalSync
               && (string.Equals(snapshotHash, _lastSavedSnapshotHash, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(snapshotHash, _remoteMetadata?.PayloadHash, StringComparison.OrdinalIgnoreCase));
    }

    private static string ComputeSnapshotHash(CatalogWorkspaceSnapshot snapshot)
    {
        var json = JsonSerializer.Serialize(snapshot, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    private void EnsureBackplaneReady(string currentOperator)
    {
        if (!_serverModeEnabled)
        {
            return;
        }

        if (TryGetBackplane() is null)
        {
            return;
        }

        if (_backplane is null)
        {
            throw new InvalidOperationException("Включен режим общей БД, но подключение к серверу недоступно. Локальная загрузка товаров отключена.");
        }

        try
        {
            _backplane.EnsureReady(currentOperator);
        }
        catch
        {
        }
    }

    private DesktopMySqlBackplaneService? TryGetBackplane()
    {
        if (!_serverModeEnabled)
        {
            return _backplane;
        }

        if (_backplane?.IsConnectionHealthy == true)
        {
            return _backplane;
        }

        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is not null)
        {
            _backplane = backplane;
        }

        return _backplane?.IsConnectionHealthy == true ? _backplane : null;
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

        var priceRegistrations = MapPriceRegistrationsFromImport(salesWorkspace, priceTypes);

        return new CatalogWorkspaceSeed
        {
            Items = items,
            PriceTypes = priceTypes,
            Discounts = Array.Empty<CatalogDiscountRecord>(),
            PriceRegistrations = priceRegistrations,
            Currencies = currencies,
            Warehouses = warehouses
        };
    }

    /// <summary>
    /// Маппит документы «Установка цен номенклатуры» из 1С выгрузки в наши
    /// <see cref="CatalogPriceRegistrationRecord"/>. Каждый документ становится
    /// одной записью с табличной частью «Товары» → CatalogPriceRegistrationLineRecord.
    /// </summary>
    private static IReadOnlyList<CatalogPriceRegistrationRecord> MapPriceRegistrationsFromImport(
        SalesWorkspace salesWorkspace,
        IReadOnlyList<CatalogPriceTypeRecord> priceTypes)
    {
        var importRecords = salesWorkspace.OneCImport?.PriceRegistrations.Records;
        if (importRecords is null || importRecords.Count == 0)
        {
            return Array.Empty<CatalogPriceRegistrationRecord>();
        }

        var defaultCurrency = priceTypes.FirstOrDefault()?.CurrencyCode
            ?? salesWorkspace.Currencies.FirstOrDefault()
            ?? "RUB";

        var result = new List<CatalogPriceRegistrationRecord>(importRecords.Count);
        foreach (var record in importRecords)
        {
            if (string.IsNullOrWhiteSpace(record.Reference) && string.IsNullOrWhiteSpace(record.Number))
            {
                continue;
            }

            var priceTypeName = FirstNonEmpty(
                GetFieldDisplay(record, "ВидЦен", "ВидЦены"),
                priceTypes.FirstOrDefault()?.Name ?? string.Empty);
            var currencyCode = FirstNonEmpty(
                GetFieldDisplay(record, "Валюта", "ВалютаДокумента"),
                defaultCurrency);
            var status = string.IsNullOrWhiteSpace(record.Status) ? "Проведен" : record.Status;

            var lines = new List<CatalogPriceRegistrationLineRecord>();
            foreach (var section in record.TabularSections)
            {
                if (!string.Equals(section.Name, "Товары", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(section.Name, "Цены", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (var row in section.Rows)
                {
                    var itemDisplay = FirstNonEmpty(
                        GetRowFieldDisplay(row, "Номенклатура", "Товар"),
                        GetRowFieldDisplay(row, "Наименование"));
                    var itemCode = FirstNonEmpty(
                        GetRowFieldDisplay(row, "КодНоменклатуры", "Код"),
                        GetRowFieldDisplay(row, "Артикул"));
                    var newPrice = ParseDecimal(GetRowFieldDisplay(row, "Цена", "НоваяЦена", "Сумма"));
                    var previousPrice = ParseDecimal(GetRowFieldDisplay(row, "ПредыдущаяЦена", "СтараяЦена"));

                    if (newPrice <= 0m && previousPrice <= 0m)
                    {
                        continue;
                    }

                    lines.Add(new CatalogPriceRegistrationLineRecord
                    {
                        Id = Guid.NewGuid(),
                        ItemCode = itemCode,
                        ItemName = itemDisplay,
                        Unit = GetRowFieldDisplay(row, "ЕдиницаИзмерения", "ЕдИзмерения", "Ед"),
                        NewPrice = newPrice,
                        PreviousPrice = previousPrice
                    });
                }
            }

            if (lines.Count == 0)
            {
                continue;
            }

            result.Add(new CatalogPriceRegistrationRecord
            {
                Id = CreateDeterministicGuid($"catalog-price-registration|{record.Reference}|{record.Number}"),
                Number = record.Number,
                DocumentDate = record.Date ?? DateTime.Today,
                PriceTypeName = priceTypeName,
                CurrencyCode = currencyCode,
                Status = status,
                Comment = GetFieldDisplay(record, "Комментарий"),
                Lines = lines
            });
        }

        return result;
    }

    private static string GetRowFieldDisplay(OneCTabularSectionRowSnapshot row, params string[] names)
    {
        foreach (var name in names)
        {
            var value = row.FindField(name)?.DisplayValue;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return TextMojibakeFixer.NormalizeText(value);
            }
        }
        return string.Empty;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0m;
        }

        var normalized = value.Trim()
            .Replace(' ', ' ')
            .Replace(" ", string.Empty)
            .Replace(',', '.');

        return decimal.TryParse(
            normalized,
            System.Globalization.NumberStyles.AllowDecimalPoint | System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture,
            out var result)
            ? result
            : 0m;
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

    internal sealed class CatalogWorkspaceSnapshot
    {
        public string CurrentOperator { get; set; } = string.Empty;

        public List<CatalogItemRecord> Items { get; set; } = [];

        public List<CatalogPriceTypeRecord> PriceTypes { get; set; } = [];

        public List<CatalogDiscountRecord> Discounts { get; set; } = [];

        public List<CatalogPriceRegistrationRecord> PriceRegistrations { get; set; } = [];

        public List<CatalogOperationLogEntry> OperationLog { get; set; } = [];

        public List<CatalogItemPriceRecord> ItemPrices { get; set; } = [];

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
                ItemPrices = workspace.ItemPrices.Select(item => item.Clone()).ToList(),
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
                    ItemPrices = ItemPrices.Select(item => item.Clone()).ToArray(),
                    Currencies = Currencies.Count > 0 ? Currencies.ToArray() : fallbackCurrencies ?? Array.Empty<string>(),
                    Warehouses = Warehouses.Count > 0 ? Warehouses.ToArray() : fallbackWarehouses ?? Array.Empty<string>()
                });
        }
    }
}
