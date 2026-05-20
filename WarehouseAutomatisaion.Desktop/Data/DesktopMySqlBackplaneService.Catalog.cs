using System.Text.Json;
using MySqlConnector;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed partial class DesktopMySqlBackplaneService
{
    private const string CatalogModuleCode = "catalog";
    private const int MysqlCatalogCommandTimeoutSeconds = 90;

    internal DesktopModuleSnapshotRecord<CatalogWorkspaceStore.CatalogWorkspaceSnapshot>? TryLoadCatalogWorkspaceSnapshotRecord()
    {
        try
        {
            EnsureDatabaseAndSchema();
            var metadata = LoadCatalogWorkspaceStateMetadata();
            if (metadata is null)
            {
                return null;
            }

            var snapshot = LoadCatalogWorkspaceSnapshotRows();
            snapshot.CurrentOperator = metadata.UpdatedBy;
            return new DesktopModuleSnapshotRecord<CatalogWorkspaceStore.CatalogWorkspaceSnapshot>(snapshot, metadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    internal DesktopModuleSnapshotMetadata? TryLoadCatalogWorkspaceSnapshotMetadata()
    {
        try
        {
            EnsureDatabaseAndSchema();
            return LoadCatalogWorkspaceStateMetadata();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    internal DesktopModuleSnapshotSaveResult TrySaveCatalogWorkspaceSnapshot(
        CatalogWorkspaceStore.CatalogWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            var metadata = SaveCatalogWorkspaceSnapshot(snapshot, actorName, expectedMetadata, auditEvents);
            return DesktopModuleSnapshotSaveResult.Saved(metadata);
        }
        catch (DesktopModuleSnapshotConflictException exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Conflict(exception.ServerMetadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return DesktopModuleSnapshotSaveResult.Failed(exception.Message);
        }
    }

    private DesktopModuleSnapshotMetadata SaveCatalogWorkspaceSnapshot(
        CatalogWorkspaceStore.CatalogWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var moduleCode = NormalizeModuleCode(CatalogModuleCode);
        var actor = NormalizeUserName(actorName);
        var payloadHash = ComputeSha256(JsonSerializer.Serialize(snapshot, JsonOptions));

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlCatalogCommandTimeoutSeconds);
        using var transaction = connection.BeginTransaction();

        var currentMetadata = LoadCatalogWorkspaceStateMetadata(connection, transaction);
        if (currentMetadata is not null
            && string.Equals(currentMetadata.PayloadHash, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            transaction.Rollback();
            return currentMetadata;
        }

        if (expectedMetadata is null)
        {
            if (currentMetadata is not null)
            {
                throw new DesktopModuleSnapshotConflictException(
                    "Catalog workspace rows were changed by another client.",
                    currentMetadata);
            }
        }
        else if (currentMetadata is null
                 || currentMetadata.VersionNo != expectedMetadata.VersionNo
                 || !string.Equals(currentMetadata.PayloadHash, expectedMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopModuleSnapshotConflictException(
                "Catalog workspace rows were changed by another client.",
                currentMetadata);
        }

        ReplaceCatalogWorkspaceRows(connection, transaction, snapshot);

        var nextVersionNo = currentMetadata is null ? 1 : currentMetadata.VersionNo + 1;
        using (var stateCommand = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_module_states (
                module_code,
                payload_hash,
                version_no,
                updated_by,
                created_at_utc,
                updated_at_utc
            )
            VALUES (
                @module_code,
                @payload_hash,
                @version_no,
                @updated_by,
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                payload_hash = VALUES(payload_hash),
                version_no = VALUES(version_no),
                updated_by = VALUES(updated_by),
                updated_at_utc = UTC_TIMESTAMP(6);
            """))
        {
            AddParameter(stateCommand, "@module_code", moduleCode);
            AddParameter(stateCommand, "@payload_hash", payloadHash);
            AddParameter(stateCommand, "@version_no", nextVersionNo);
            AddParameter(stateCommand, "@updated_by", actor);
            stateCommand.ExecuteNonQuery();
        }

        transaction.Commit();

        if (auditEvents is not null)
        {
            ReplaceAuditEvents(moduleCode, auditEvents);
        }

        return LoadCatalogWorkspaceStateMetadata()
               ?? new DesktopModuleSnapshotMetadata(moduleCode, nextVersionNo, payloadHash, actor, DateTime.UtcNow);
    }

    private CatalogWorkspaceStore.CatalogWorkspaceSnapshot LoadCatalogWorkspaceSnapshotRows()
    {
        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlCatalogCommandTimeoutSeconds);

        return new CatalogWorkspaceStore.CatalogWorkspaceSnapshot
        {
            Items = LoadCatalogItems(connection),
            PriceTypes = LoadCatalogPriceTypes(connection),
            Discounts = LoadCatalogDiscounts(connection),
            // Release 1.0.138: только заголовки документов установки цен (683 строки).
            // Строки документов больше НЕ грузятся — заменены агрегатами ниже.
            PriceRegistrations = LoadCatalogPriceRegistrations(connection),
            OperationLog = LoadCatalogOperationLog(connection),
            // Release 1.0.129: 70k+ item_prices больше не грузим на старте каталога —
            // экономит ~1.5 сек по сети при первом открытии Товаров. Цены подгружаются
            // лениво по item_id когда открывается карточка товара (см. LoadPricesForItem).
            ItemPrices = new List<CatalogItemPriceRecord>(),
            // Release 1.0.138: вместо 99k строк PriceRegistration.Lines — два
            // server-side aggregate'а, итого ~6-30k строк. Покрывают всю
            // потребность ProductsView (display price + price types per item).
            LatestPrices = LoadCatalogLatestPrices(connection),
            ItemPriceTypes = LoadCatalogItemPriceTypes(connection),
            Currencies = LoadCatalogList(connection, "currency"),
            Warehouses = LoadCatalogList(connection, "warehouse")
        };
    }

    /// <summary>
    /// Release 1.0.129: lazy-load цен по конкретному товару (для карточки).
    /// Возвращает обычно ≤20 строк (по числу видов цен) — мгновенный SELECT.
    /// </summary>
    internal IReadOnlyList<CatalogItemPriceRecord> LoadPricesForItem(Guid itemId)
    {
        if (itemId == Guid.Empty)
        {
            return Array.Empty<CatalogItemPriceRecord>();
        }

        var prices = new List<CatalogItemPriceRecord>();
        try
        {
            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlCatalogCommandTimeoutSeconds);
            using var command = CreateMySqlCommand(connection, null, """
                SELECT id, item_id, price_type_id, price_type_name, price_value, currency_code
                FROM app_catalog_item_prices
                WHERE item_id = @itemId;
                """);
            AddParameter(command, "@itemId", itemId.ToString());
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                prices.Add(new CatalogItemPriceRecord
                {
                    Id = ReadGuid(reader, "id"),
                    ItemId = ReadGuid(reader, "item_id"),
                    PriceTypeId = ReadGuid(reader, "price_type_id"),
                    PriceTypeName = ReadString(reader, "price_type_name"),
                    Price = ReadDecimal(reader, "price_value"),
                    CurrencyCode = ReadString(reader, "currency_code")
                });
            }
        }
        catch (Exception ex)
        {
            TryWriteErrorLog(ex);
        }

        return prices;
    }

    private DesktopModuleSnapshotMetadata? LoadCatalogWorkspaceStateMetadata()
    {
        var sql = $"""
            SELECT COALESCE(
                CAST(
                    JSON_OBJECT(
                        'VersionNo', version_no,
                        'PayloadHash', payload_hash,
                        'UpdatedBy', updated_by,
                        'UpdatedAtUtc', DATE_FORMAT(updated_at_utc, '%Y-%m-%dT%H:%i:%s.%fZ')
                    ) AS CHAR CHARACTER SET utf8mb4
                ),
                'null'
            )
            FROM app_module_states
            WHERE module_code = {SqlUtf8TextExpression(NormalizeModuleCode(CatalogModuleCode))}
            LIMIT 1;
            """;

        var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlCatalogCommandTimeoutSeconds).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
        return row is null ? null : CreateSnapshotMetadata(CatalogModuleCode, row);
    }

    private DesktopModuleSnapshotMetadata? LoadCatalogWorkspaceStateMetadata(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            SELECT
                version_no,
                payload_hash,
                updated_by,
                updated_at_utc
            FROM app_module_states
            WHERE module_code = @module_code
            LIMIT 1;
            """);
        AddParameter(command, "@module_code", NormalizeModuleCode(CatalogModuleCode));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DesktopModuleSnapshotMetadata(
            NormalizeModuleCode(CatalogModuleCode),
            reader.GetInt32(reader.GetOrdinal("version_no")),
            ReadString(reader, "payload_hash"),
            ReadString(reader, "updated_by"),
            DateTime.SpecifyKind(ReadDateTime(reader, "updated_at_utc"), DateTimeKind.Utc));
    }

    private void ReplaceCatalogWorkspaceRows(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CatalogWorkspaceStore.CatalogWorkspaceSnapshot snapshot)
    {
        var items = snapshot.Items ?? [];
        var priceTypes = snapshot.PriceTypes ?? [];
        var discounts = snapshot.Discounts ?? [];
        var priceRegistrations = snapshot.PriceRegistrations ?? [];
        var operationLog = snapshot.OperationLog ?? [];
        var itemPrices = snapshot.ItemPrices ?? [];

        CreateCatalogKeepTables(connection, transaction);
        PopulateCatalogKeepTables(connection, transaction, items, priceTypes, discounts, priceRegistrations, operationLog, itemPrices);
        DeleteMissingCatalogRows(connection, transaction);
        ReplaceCatalogLookupLists(connection, transaction, snapshot.Currencies ?? [], snapshot.Warehouses ?? []);

        InsertCatalogItems(connection, transaction, items);
        InsertCatalogPriceTypes(connection, transaction, priceTypes);
        InsertCatalogDiscounts(connection, transaction, discounts);
        InsertCatalogPriceRegistrations(connection, transaction, priceRegistrations);
        InsertCatalogOperationLog(connection, transaction, operationLog);
        InsertCatalogItemPrices(connection, transaction, itemPrices);
    }

    private static void CreateCatalogKeepTables(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        foreach (var tableName in new[]
                 {
                     "tmp_app_catalog_keep_items",
                     "tmp_app_catalog_keep_price_types",
                     "tmp_app_catalog_keep_discounts",
                     "tmp_app_catalog_keep_price_registrations",
                     "tmp_app_catalog_keep_price_registration_lines",
                     "tmp_app_catalog_keep_operation_log",
                     "tmp_app_catalog_keep_item_prices"
                 })
        {
            ExecuteMySqlNonQuery(connection, transaction, $"CREATE TEMPORARY TABLE {tableName} (id CHAR(36) NOT NULL PRIMARY KEY) ENGINE=MEMORY;");
        }
    }

    private static void PopulateCatalogKeepTables(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogItemRecord> items,
        IEnumerable<CatalogPriceTypeRecord> priceTypes,
        IEnumerable<CatalogDiscountRecord> discounts,
        IEnumerable<CatalogPriceRegistrationRecord> priceRegistrations,
        IEnumerable<CatalogOperationLogEntry> operationLog,
        IEnumerable<CatalogItemPriceRecord> itemPrices)
    {
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_items", items
            .Select(item => EnsureId(item.Id, $"catalog-item|{item.Code}|{item.Name}")));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_price_types", priceTypes
            .Select(item => EnsureId(item.Id, $"catalog-price-type|{item.Code}|{item.Name}")));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_discounts", discounts
            .Select(item => EnsureId(item.Id, $"catalog-discount|{item.Name}|{item.PriceTypeName}|{item.Period}")));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_price_registrations", priceRegistrations
            .Select(item => EnsureId(item.Id, $"catalog-price-registration|{item.Number}")));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_price_registration_lines", EnumerateCatalogPriceRegistrationLineIds(priceRegistrations));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_operation_log", operationLog
            .Take(500)
            .Select(item => EnsureId(item.Id, $"catalog-log|{item.EntityNumber}|{item.Action}|{item.LoggedAt:O}")));
        InsertKeepIds(connection, transaction, "tmp_app_catalog_keep_item_prices", itemPrices
            .Where(price => price.ItemId != Guid.Empty && price.PriceTypeId != Guid.Empty)
            .Select(price => EnsureId(price.Id, $"catalog-item-price|{price.ItemId:N}|{price.PriceTypeId:N}")));
    }

    private static IEnumerable<Guid> EnumerateCatalogPriceRegistrationLineIds(
        IEnumerable<CatalogPriceRegistrationRecord> priceRegistrations)
    {
        foreach (var document in priceRegistrations)
        {
            var documentId = EnsureId(document.Id, $"catalog-price-registration|{document.Number}");
            var lineNo = 1;
            foreach (var line in document.Lines ?? [])
            {
                yield return EnsureId(line.Id, $"{documentId:N}|price-registration-line|{lineNo}");
                lineNo++;
            }
        }
    }

    private static void DeleteMissingCatalogRows(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_operation_log target
            LEFT JOIN tmp_app_catalog_keep_operation_log keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_price_registration_lines target
            LEFT JOIN tmp_app_catalog_keep_price_registration_lines keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_price_registrations target
            LEFT JOIN tmp_app_catalog_keep_price_registrations keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_discounts target
            LEFT JOIN tmp_app_catalog_keep_discounts keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_price_types target
            LEFT JOIN tmp_app_catalog_keep_price_types keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        // item_prices удаляем ДО items: на app_catalog_item_prices.item_id висит ON DELETE CASCADE,
        // но cascade сработает и удалит цены, которые мы могли захотеть оставить под новым ItemId.
        // Безопаснее почистить явно по keep-таблице.
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_item_prices target
            LEFT JOIN tmp_app_catalog_keep_item_prices keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE target
            FROM app_catalog_items target
            LEFT JOIN tmp_app_catalog_keep_items keep_rows ON keep_rows.id = target.id
            WHERE keep_rows.id IS NULL;
            """);
    }

    private static void ReplaceCatalogLookupLists(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<string> currencies,
        IEnumerable<string> warehouses)
    {
        ExecuteMySqlNonQuery(connection, transaction, """
            DELETE FROM app_catalog_lists
            WHERE list_kind IN ('currency', 'warehouse');
            """);

        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_lists (
                list_kind,
                value_text,
                sort_order
            )
            VALUES (
                @list_kind,
                @value_text,
                @sort_order
            );
            """);
        AddParameter(command, "@list_kind");
        AddParameter(command, "@value_text");
        AddParameter(command, "@sort_order");

        var sortOrder = 0;
        foreach (var value in currencies.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SetParameter(command, "@list_kind", "currency");
            SetParameter(command, "@value_text", value);
            SetParameter(command, "@sort_order", sortOrder++);
            command.ExecuteNonQuery();
        }

        sortOrder = 0;
        foreach (var value in warehouses.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            SetParameter(command, "@list_kind", "warehouse");
            SetParameter(command, "@value_text", value);
            SetParameter(command, "@sort_order", sortOrder++);
            command.ExecuteNonQuery();
        }
    }

    private static List<CatalogItemRecord> LoadCatalogItems(MySqlConnection connection)
    {
        var items = new List<CatalogItemRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                code,
                name,
                unit_name,
                category_name,
                supplier_name,
                default_warehouse,
                status_text,
                currency_code,
                default_price,
                barcode_value,
                barcode_format,
                qr_payload,
                notes,
                source_label,
                item_type,
                name_for_print,
                parent_group,
                brand,
                color,
                description_text,
                width_cm,
                height_cm,
                depth_cm,
                weight_kg,
                is_weight,
                forbid_fractional,
                min_sale_quantity,
                pack_quantity,
                site_upload_status,
                is_inactive
            FROM app_catalog_items
            ORDER BY name, code;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            items.Add(new CatalogItemRecord
            {
                Id = ReadGuid(reader, "id"),
                Code = ReadString(reader, "code"),
                Name = ReadString(reader, "name"),
                Unit = ReadString(reader, "unit_name"),
                Category = ReadString(reader, "category_name"),
                Supplier = ReadString(reader, "supplier_name"),
                DefaultWarehouse = ReadString(reader, "default_warehouse"),
                Status = ReadString(reader, "status_text"),
                CurrencyCode = ReadString(reader, "currency_code"),
                DefaultPrice = ReadDecimal(reader, "default_price"),
                BarcodeValue = ReadString(reader, "barcode_value"),
                BarcodeFormat = ReadString(reader, "barcode_format"),
                QrPayload = ReadString(reader, "qr_payload"),
                Notes = ReadString(reader, "notes"),
                SourceLabel = ReadString(reader, "source_label"),
                ItemType = ReadString(reader, "item_type"),
                NameForPrint = ReadString(reader, "name_for_print"),
                ParentGroup = ReadString(reader, "parent_group"),
                Brand = ReadString(reader, "brand"),
                Color = ReadString(reader, "color"),
                Description = ReadString(reader, "description_text"),
                WidthCm = ReadDecimal(reader, "width_cm"),
                HeightCm = ReadDecimal(reader, "height_cm"),
                DepthCm = ReadDecimal(reader, "depth_cm"),
                WeightKg = ReadDecimal(reader, "weight_kg"),
                IsWeight = ReadBoolean(reader, "is_weight"),
                ForbidFractional = ReadBoolean(reader, "forbid_fractional"),
                MinSaleQuantity = ReadDecimal(reader, "min_sale_quantity"),
                PackQuantity = ReadDecimal(reader, "pack_quantity"),
                SiteUploadStatus = ReadString(reader, "site_upload_status"),
                IsInactive = ReadBoolean(reader, "is_inactive")
            });
        }

        return items;
    }

    private static List<CatalogItemPriceRecord> LoadCatalogItemPrices(MySqlConnection connection)
    {
        var prices = new List<CatalogItemPriceRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                item_id,
                price_type_id,
                price_type_name,
                price_value,
                currency_code
            FROM app_catalog_item_prices;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            prices.Add(new CatalogItemPriceRecord
            {
                Id = ReadGuid(reader, "id"),
                ItemId = ReadGuid(reader, "item_id"),
                PriceTypeId = ReadGuid(reader, "price_type_id"),
                PriceTypeName = ReadString(reader, "price_type_name"),
                Price = ReadDecimal(reader, "price_value"),
                CurrencyCode = ReadString(reader, "currency_code")
            });
        }

        return prices;
    }

    private static List<CatalogPriceTypeRecord> LoadCatalogPriceTypes(MySqlConnection connection)
    {
        var priceTypes = new List<CatalogPriceTypeRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                code,
                name,
                currency_code,
                base_price_type_name,
                rounding_rule,
                is_default,
                is_manual_entry_only,
                uses_psychological_rounding,
                status_text
            FROM app_catalog_price_types
            ORDER BY is_default DESC, name, code;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            priceTypes.Add(new CatalogPriceTypeRecord
            {
                Id = ReadGuid(reader, "id"),
                Code = ReadString(reader, "code"),
                Name = ReadString(reader, "name"),
                CurrencyCode = ReadString(reader, "currency_code"),
                BasePriceTypeName = ReadString(reader, "base_price_type_name"),
                RoundingRule = ReadString(reader, "rounding_rule"),
                IsDefault = ReadBoolean(reader, "is_default"),
                IsManualEntryOnly = ReadBoolean(reader, "is_manual_entry_only"),
                UsesPsychologicalRounding = ReadBoolean(reader, "uses_psychological_rounding"),
                Status = ReadString(reader, "status_text")
            });
        }

        return priceTypes;
    }

    private static List<CatalogDiscountRecord> LoadCatalogDiscounts(MySqlConnection connection)
    {
        var discounts = new List<CatalogDiscountRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                name,
                percent_value,
                price_type_name,
                period_text,
                scope_text,
                status_text,
                comment_text
            FROM app_catalog_discounts
            ORDER BY name, price_type_name;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            discounts.Add(new CatalogDiscountRecord
            {
                Id = ReadGuid(reader, "id"),
                Name = ReadString(reader, "name"),
                Percent = ReadDecimal(reader, "percent_value"),
                PriceTypeName = ReadString(reader, "price_type_name"),
                Period = ReadString(reader, "period_text"),
                Scope = ReadString(reader, "scope_text"),
                Status = ReadString(reader, "status_text"),
                Comment = ReadString(reader, "comment_text")
            });
        }

        return discounts;
    }

    private static List<CatalogPriceRegistrationRecord> LoadCatalogPriceRegistrations(MySqlConnection connection)
    {
        // Release 1.0.138: грузим ТОЛЬКО заголовки документов (683 строки).
        // Строки документов (99k штук) теперь НЕ грузим вообще — они нужны были
        // только для in-memory расчёта «последней цены per item» в ProductsView.
        // Эту работу делает SQL aggregate (см. LoadCatalogLatestPrices /
        // LoadCatalogItemPriceTypes), результат — ~6-12k строк per aggregate.
        // Вкладка «Установка цен» (PriceRegistrationsWorkspaceView) использует
        // только Date/Number/PriceTypes/Comment из заголовка — Lines ей не нужны.
        // Если в будущем понадобится drill-down по строкам конкретного документа,
        // делать его lazy-load'ом по document_id через отдельный SELECT.
        var registrations = new List<CatalogPriceRegistrationRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                number,
                document_date,
                price_type_name,
                currency_code,
                status_text,
                comment_text
            FROM app_catalog_price_registrations
            ORDER BY document_date DESC, number DESC;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            registrations.Add(new CatalogPriceRegistrationRecord
            {
                Id = ReadGuid(reader, "id"),
                Number = ReadString(reader, "number"),
                DocumentDate = ReadDateTime(reader, "document_date"),
                PriceTypeName = ReadString(reader, "price_type_name"),
                CurrencyCode = ReadString(reader, "currency_code"),
                Status = ReadString(reader, "status_text"),
                Comment = ReadString(reader, "comment_text"),
                Lines = []
            });
        }

        return registrations;
    }

    /// <summary>
    /// Release 1.0.138: SQL aggregate «лучшая цена per item». Заменяет проход
    /// ProductsView по 99k строкам PriceRegistration.Lines с приоритезацией
    /// (Розничная > прочие) и поиском последней даты. Возвращает ~6-12k строк.
    /// </summary>
    private static List<CatalogLatestPriceRecord> LoadCatalogLatestPrices(MySqlConnection connection)
    {
        var result = new List<CatalogLatestPriceRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                l.item_code,
                l.item_name,
                d.price_type_name,
                l.new_price,
                d.document_date
            FROM (
                SELECT
                    l.id,
                    ROW_NUMBER() OVER (
                        PARTITION BY
                            COALESCE(NULLIF(TRIM(l.item_code), ''), CONCAT('@name=', COALESCE(NULLIF(TRIM(l.item_name), ''), ''))),
                            COALESCE(NULLIF(TRIM(l.item_name), ''), '')
                        ORDER BY
                            CASE WHEN d.price_type_name LIKE '%озничн%' THEN 2 ELSE 1 END DESC,
                            d.document_date DESC,
                            d.id DESC
                    ) AS rn
                FROM app_catalog_price_registration_lines l
                JOIN app_catalog_price_registrations d ON d.id = l.registration_id
                WHERE l.new_price > 0
            ) ranked
            JOIN app_catalog_price_registration_lines l ON l.id = ranked.id
            JOIN app_catalog_price_registrations d ON d.id = l.registration_id
            WHERE ranked.rn = 1;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CatalogLatestPriceRecord
            {
                ItemCode = ReadString(reader, "item_code"),
                ItemName = ReadString(reader, "item_name"),
                PriceTypeName = ReadString(reader, "price_type_name"),
                Price = ReadDecimal(reader, "new_price"),
                DocumentDate = ReadDateTime(reader, "document_date")
            });
        }

        return result;
    }

    /// <summary>
    /// Release 1.0.138: distinct-комбинации (item, price_type). Используется
    /// для filter-combo «Виды цен» во вкладке Товары. Возвращает ≤ items × priceTypes.
    /// </summary>
    private static List<CatalogItemPriceTypeRecord> LoadCatalogItemPriceTypes(MySqlConnection connection)
    {
        var result = new List<CatalogItemPriceTypeRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT DISTINCT
                l.item_code,
                l.item_name,
                d.price_type_name
            FROM app_catalog_price_registration_lines l
            JOIN app_catalog_price_registrations d ON d.id = l.registration_id
            WHERE l.new_price > 0
                AND d.price_type_name IS NOT NULL
                AND d.price_type_name <> '';
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            result.Add(new CatalogItemPriceTypeRecord
            {
                ItemCode = ReadString(reader, "item_code"),
                ItemName = ReadString(reader, "item_name"),
                PriceTypeName = ReadString(reader, "price_type_name")
            });
        }

        return result;
    }

    private static List<CatalogOperationLogEntry> LoadCatalogOperationLog(MySqlConnection connection)
    {
        var entries = new List<CatalogOperationLogEntry>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                logged_at,
                actor_user_name,
                entity_type,
                entity_id,
                entity_number,
                action_text,
                result_text,
                message_text
            FROM app_catalog_operation_log
            ORDER BY logged_at DESC
            LIMIT 500;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new CatalogOperationLogEntry
            {
                Id = ReadGuid(reader, "id"),
                LoggedAt = ReadDateTime(reader, "logged_at"),
                Actor = ReadString(reader, "actor_user_name"),
                EntityType = ReadString(reader, "entity_type"),
                EntityId = ReadGuid(reader, "entity_id"),
                EntityNumber = ReadString(reader, "entity_number"),
                Action = ReadString(reader, "action_text"),
                Result = ReadString(reader, "result_text"),
                Message = ReadString(reader, "message_text")
            });
        }

        return entries;
    }

    private static List<string> LoadCatalogList(MySqlConnection connection, string listKind)
    {
        var values = new List<string>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT value_text
            FROM app_catalog_lists
            WHERE list_kind = @list_kind
            ORDER BY sort_order, value_text;
            """);
        AddParameter(command, "@list_kind", listKind);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(ReadString(reader, "value_text"));
        }

        return values;
    }

    private static void InsertCatalogItems(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogItemRecord> items)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_items (
                id,
                code,
                name,
                unit_name,
                category_name,
                supplier_name,
                default_warehouse,
                status_text,
                currency_code,
                default_price,
                barcode_value,
                barcode_format,
                qr_payload,
                notes,
                source_label,
                item_type,
                name_for_print,
                parent_group,
                brand,
                color,
                description_text,
                width_cm,
                height_cm,
                depth_cm,
                weight_kg,
                is_weight,
                forbid_fractional,
                min_sale_quantity,
                pack_quantity,
                site_upload_status,
                is_inactive
            )
            VALUES (
                @id,
                @code,
                @name,
                @unit_name,
                @category_name,
                @supplier_name,
                @default_warehouse,
                @status_text,
                @currency_code,
                @default_price,
                @barcode_value,
                @barcode_format,
                @qr_payload,
                @notes,
                @source_label,
                @item_type,
                @name_for_print,
                @parent_group,
                @brand,
                @color,
                @description_text,
                @width_cm,
                @height_cm,
                @depth_cm,
                @weight_kg,
                @is_weight,
                @forbid_fractional,
                @min_sale_quantity,
                @pack_quantity,
                @site_upload_status,
                @is_inactive
            )
            ON DUPLICATE KEY UPDATE
                code = VALUES(code),
                name = VALUES(name),
                unit_name = VALUES(unit_name),
                category_name = VALUES(category_name),
                supplier_name = VALUES(supplier_name),
                default_warehouse = VALUES(default_warehouse),
                status_text = VALUES(status_text),
                currency_code = VALUES(currency_code),
                default_price = VALUES(default_price),
                barcode_value = VALUES(barcode_value),
                barcode_format = VALUES(barcode_format),
                qr_payload = VALUES(qr_payload),
                notes = VALUES(notes),
                source_label = VALUES(source_label),
                item_type = VALUES(item_type),
                name_for_print = VALUES(name_for_print),
                parent_group = VALUES(parent_group),
                brand = VALUES(brand),
                color = VALUES(color),
                description_text = VALUES(description_text),
                width_cm = VALUES(width_cm),
                height_cm = VALUES(height_cm),
                depth_cm = VALUES(depth_cm),
                weight_kg = VALUES(weight_kg),
                is_weight = VALUES(is_weight),
                forbid_fractional = VALUES(forbid_fractional),
                min_sale_quantity = VALUES(min_sale_quantity),
                pack_quantity = VALUES(pack_quantity),
                site_upload_status = VALUES(site_upload_status),
                is_inactive = VALUES(is_inactive);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@code", "@name", "@unit_name", "@category_name", "@supplier_name",
                     "@default_warehouse", "@status_text", "@currency_code", "@default_price",
                     "@barcode_value", "@barcode_format", "@qr_payload", "@notes", "@source_label",
                     "@item_type", "@name_for_print", "@parent_group", "@brand", "@color",
                     "@description_text", "@width_cm", "@height_cm", "@depth_cm", "@weight_kg",
                     "@is_weight", "@forbid_fractional", "@min_sale_quantity", "@pack_quantity",
                     "@site_upload_status", "@is_inactive"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var item in items)
        {
            var itemId = EnsureId(item.Id, $"catalog-item|{item.Code}|{item.Name}");
            SetParameter(command, "@id", itemId.ToString());
            SetParameter(command, "@code", item.Code ?? string.Empty);
            SetParameter(command, "@name", item.Name ?? string.Empty);
            SetParameter(command, "@unit_name", item.Unit ?? string.Empty);
            SetParameter(command, "@category_name", item.Category ?? string.Empty);
            SetParameter(command, "@supplier_name", item.Supplier ?? string.Empty);
            SetParameter(command, "@default_warehouse", item.DefaultWarehouse ?? string.Empty);
            SetParameter(command, "@status_text", item.Status ?? string.Empty);
            SetParameter(command, "@currency_code", string.IsNullOrWhiteSpace(item.CurrencyCode) ? "RUB" : item.CurrencyCode);
            SetParameter(command, "@default_price", item.DefaultPrice);
            SetParameter(command, "@barcode_value", item.BarcodeValue ?? string.Empty);
            SetParameter(command, "@barcode_format", item.BarcodeFormat ?? string.Empty);
            SetParameter(command, "@qr_payload", item.QrPayload ?? string.Empty);
            SetParameter(command, "@notes", item.Notes ?? string.Empty);
            SetParameter(command, "@source_label", item.SourceLabel ?? string.Empty);
            SetParameter(command, "@item_type", string.IsNullOrWhiteSpace(item.ItemType) ? "Запас" : item.ItemType);
            SetParameter(command, "@name_for_print", item.NameForPrint ?? string.Empty);
            SetParameter(command, "@parent_group", item.ParentGroup ?? string.Empty);
            SetParameter(command, "@brand", item.Brand ?? string.Empty);
            SetParameter(command, "@color", item.Color ?? string.Empty);
            SetParameter(command, "@description_text", item.Description ?? string.Empty);
            SetParameter(command, "@width_cm", item.WidthCm);
            SetParameter(command, "@height_cm", item.HeightCm);
            SetParameter(command, "@depth_cm", item.DepthCm);
            SetParameter(command, "@weight_kg", item.WeightKg);
            SetParameter(command, "@is_weight", item.IsWeight ? 1 : 0);
            SetParameter(command, "@forbid_fractional", item.ForbidFractional ? 1 : 0);
            SetParameter(command, "@min_sale_quantity", item.MinSaleQuantity);
            SetParameter(command, "@pack_quantity", item.PackQuantity);
            SetParameter(command, "@site_upload_status", item.SiteUploadStatus ?? string.Empty);
            SetParameter(command, "@is_inactive", item.IsInactive ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCatalogItemPrices(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogItemPriceRecord> prices)
    {
        // Release 1.0.108: batch multi-row INSERT (по 500 строк за запрос).
        // Раньше per-row INSERT на 70 935 строк = десятки тысяч round-trip → SocketException 10054.
        var list = prices.Where(p => p.ItemId != Guid.Empty && p.PriceTypeId != Guid.Empty).ToList();
        if (list.Count == 0)
        {
            return;
        }

        const int batchSize = 500;
        for (int offset = 0; offset < list.Count; offset += batchSize)
        {
            var sliceCount = Math.Min(batchSize, list.Count - offset);
            var sb = new System.Text.StringBuilder(@"INSERT INTO app_catalog_item_prices (id, item_id, price_type_id, price_type_name, price_value, currency_code) VALUES ");
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandTimeout = MysqlSalesCommandTimeoutSeconds;

            for (int i = 0; i < sliceCount; i++)
            {
                var price = list[offset + i];
                var priceId = EnsureId(price.Id, $"catalog-item-price|{price.ItemId:N}|{price.PriceTypeId:N}");
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("(@id").Append(i)
                  .Append(",@iid").Append(i)
                  .Append(",@ptid").Append(i)
                  .Append(",@ptn").Append(i)
                  .Append(",@pv").Append(i)
                  .Append(",@cc").Append(i).Append(')');
                command.Parameters.AddWithValue($"@id{i}", priceId.ToString());
                command.Parameters.AddWithValue($"@iid{i}", price.ItemId.ToString());
                command.Parameters.AddWithValue($"@ptid{i}", price.PriceTypeId.ToString());
                command.Parameters.AddWithValue($"@ptn{i}", price.PriceTypeName ?? string.Empty);
                command.Parameters.AddWithValue($"@pv{i}", price.Price);
                command.Parameters.AddWithValue($"@cc{i}", string.IsNullOrWhiteSpace(price.CurrencyCode) ? "RUB" : price.CurrencyCode);
            }

            sb.Append(@" ON DUPLICATE KEY UPDATE price_type_name = VALUES(price_type_name), price_value = VALUES(price_value), currency_code = VALUES(currency_code);");
            command.CommandText = sb.ToString();
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCatalogPriceTypes(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogPriceTypeRecord> priceTypes)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_price_types (
                id,
                code,
                name,
                currency_code,
                base_price_type_name,
                rounding_rule,
                is_default,
                is_manual_entry_only,
                uses_psychological_rounding,
                status_text
            )
            VALUES (
                @id,
                @code,
                @name,
                @currency_code,
                @base_price_type_name,
                @rounding_rule,
                @is_default,
                @is_manual_entry_only,
                @uses_psychological_rounding,
                @status_text
            )
            ON DUPLICATE KEY UPDATE
                code = VALUES(code),
                name = VALUES(name),
                currency_code = VALUES(currency_code),
                base_price_type_name = VALUES(base_price_type_name),
                rounding_rule = VALUES(rounding_rule),
                is_default = VALUES(is_default),
                is_manual_entry_only = VALUES(is_manual_entry_only),
                uses_psychological_rounding = VALUES(uses_psychological_rounding),
                status_text = VALUES(status_text);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@code", "@name", "@currency_code", "@base_price_type_name",
                     "@rounding_rule", "@is_default", "@is_manual_entry_only",
                     "@uses_psychological_rounding", "@status_text"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var priceType in priceTypes)
        {
            var priceTypeId = EnsureId(priceType.Id, $"catalog-price-type|{priceType.Code}|{priceType.Name}");
            SetParameter(command, "@id", priceTypeId.ToString());
            SetParameter(command, "@code", priceType.Code ?? string.Empty);
            SetParameter(command, "@name", priceType.Name ?? string.Empty);
            SetParameter(command, "@currency_code", string.IsNullOrWhiteSpace(priceType.CurrencyCode) ? "RUB" : priceType.CurrencyCode);
            SetParameter(command, "@base_price_type_name", priceType.BasePriceTypeName ?? string.Empty);
            SetParameter(command, "@rounding_rule", priceType.RoundingRule ?? string.Empty);
            SetParameter(command, "@is_default", priceType.IsDefault ? 1 : 0);
            SetParameter(command, "@is_manual_entry_only", priceType.IsManualEntryOnly ? 1 : 0);
            SetParameter(command, "@uses_psychological_rounding", priceType.UsesPsychologicalRounding ? 1 : 0);
            SetParameter(command, "@status_text", priceType.Status ?? string.Empty);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCatalogDiscounts(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogDiscountRecord> discounts)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_discounts (
                id,
                name,
                percent_value,
                price_type_name,
                period_text,
                scope_text,
                status_text,
                comment_text
            )
            VALUES (
                @id,
                @name,
                @percent_value,
                @price_type_name,
                @period_text,
                @scope_text,
                @status_text,
                @comment_text
            )
            ON DUPLICATE KEY UPDATE
                name = VALUES(name),
                percent_value = VALUES(percent_value),
                price_type_name = VALUES(price_type_name),
                period_text = VALUES(period_text),
                scope_text = VALUES(scope_text),
                status_text = VALUES(status_text),
                comment_text = VALUES(comment_text);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@name", "@percent_value", "@price_type_name",
                     "@period_text", "@scope_text", "@status_text", "@comment_text"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var discount in discounts)
        {
            var discountId = EnsureId(discount.Id, $"catalog-discount|{discount.Name}|{discount.PriceTypeName}|{discount.Period}");
            SetParameter(command, "@id", discountId.ToString());
            SetParameter(command, "@name", discount.Name ?? string.Empty);
            SetParameter(command, "@percent_value", discount.Percent);
            SetParameter(command, "@price_type_name", discount.PriceTypeName ?? string.Empty);
            SetParameter(command, "@period_text", discount.Period ?? string.Empty);
            SetParameter(command, "@scope_text", discount.Scope ?? string.Empty);
            SetParameter(command, "@status_text", discount.Status ?? string.Empty);
            SetParameter(command, "@comment_text", discount.Comment ?? string.Empty);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertCatalogPriceRegistrations(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogPriceRegistrationRecord> priceRegistrations)
    {
        using var documentCommand = CreateCatalogPriceRegistrationCommand(connection, transaction);
        using var lineCommand = CreateCatalogPriceRegistrationLineCommand(connection, transaction);

        foreach (var document in priceRegistrations)
        {
            var documentId = EnsureId(document.Id, $"catalog-price-registration|{document.Number}");
            SetParameter(documentCommand, "@id", documentId.ToString());
            SetParameter(documentCommand, "@number", document.Number ?? string.Empty);
            SetParameter(documentCommand, "@document_date", document.DocumentDate == default ? DateTime.Today : document.DocumentDate);
            SetParameter(documentCommand, "@price_type_name", document.PriceTypeName ?? string.Empty);
            SetParameter(documentCommand, "@currency_code", string.IsNullOrWhiteSpace(document.CurrencyCode) ? "RUB" : document.CurrencyCode);
            SetParameter(documentCommand, "@status_text", document.Status ?? string.Empty);
            SetParameter(documentCommand, "@comment_text", document.Comment ?? string.Empty);
            documentCommand.ExecuteNonQuery();

            var lineNo = 1;
            foreach (var line in document.Lines ?? [])
            {
                var lineId = EnsureId(line.Id, $"{documentId:N}|price-registration-line|{lineNo}");
                SetParameter(lineCommand, "@id", lineId.ToString());
                SetParameter(lineCommand, "@registration_id", documentId.ToString());
                SetParameter(lineCommand, "@line_no", lineNo);
                SetParameter(lineCommand, "@item_code", line.ItemCode ?? string.Empty);
                SetParameter(lineCommand, "@item_name", line.ItemName ?? string.Empty);
                SetParameter(lineCommand, "@unit_name", line.Unit ?? string.Empty);
                SetParameter(lineCommand, "@previous_price", line.PreviousPrice);
                SetParameter(lineCommand, "@new_price", line.NewPrice);
                lineCommand.ExecuteNonQuery();
                lineNo++;
            }
        }
    }

    private static MySqlCommand CreateCatalogPriceRegistrationCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_price_registrations (
                id,
                number,
                document_date,
                price_type_name,
                currency_code,
                status_text,
                comment_text
            )
            VALUES (
                @id,
                @number,
                @document_date,
                @price_type_name,
                @currency_code,
                @status_text,
                @comment_text
            )
            ON DUPLICATE KEY UPDATE
                number = VALUES(number),
                document_date = VALUES(document_date),
                price_type_name = VALUES(price_type_name),
                currency_code = VALUES(currency_code),
                status_text = VALUES(status_text),
                comment_text = VALUES(comment_text);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@number", "@document_date", "@price_type_name",
                     "@currency_code", "@status_text", "@comment_text"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static MySqlCommand CreateCatalogPriceRegistrationLineCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_price_registration_lines (
                id,
                registration_id,
                line_no,
                item_code,
                item_name,
                unit_name,
                previous_price,
                new_price
            )
            VALUES (
                @id,
                @registration_id,
                @line_no,
                @item_code,
                @item_name,
                @unit_name,
                @previous_price,
                @new_price
            )
            ON DUPLICATE KEY UPDATE
                registration_id = VALUES(registration_id),
                line_no = VALUES(line_no),
                item_code = VALUES(item_code),
                item_name = VALUES(item_name),
                unit_name = VALUES(unit_name),
                previous_price = VALUES(previous_price),
                new_price = VALUES(new_price);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@registration_id", "@line_no", "@item_code",
                     "@item_name", "@unit_name", "@previous_price", "@new_price"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static void InsertCatalogOperationLog(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<CatalogOperationLogEntry> operationLog)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_catalog_operation_log (
                id,
                logged_at,
                actor_user_name,
                entity_type,
                entity_id,
                entity_number,
                action_text,
                result_text,
                message_text
            )
            VALUES (
                @id,
                @logged_at,
                @actor_user_name,
                @entity_type,
                @entity_id,
                @entity_number,
                @action_text,
                @result_text,
                @message_text
            )
            ON DUPLICATE KEY UPDATE
                logged_at = VALUES(logged_at),
                actor_user_name = VALUES(actor_user_name),
                entity_type = VALUES(entity_type),
                entity_id = VALUES(entity_id),
                entity_number = VALUES(entity_number),
                action_text = VALUES(action_text),
                result_text = VALUES(result_text),
                message_text = VALUES(message_text);
            """);
        foreach (var name in new[]
                 {
                     "@id", "@logged_at", "@actor_user_name", "@entity_type", "@entity_id",
                     "@entity_number", "@action_text", "@result_text", "@message_text"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var entry in operationLog.Take(500))
        {
            var entryId = EnsureId(entry.Id, $"catalog-log|{entry.EntityNumber}|{entry.Action}|{entry.LoggedAt:O}");
            SetParameter(command, "@id", entryId.ToString());
            SetParameter(command, "@logged_at", entry.LoggedAt == default ? DateTime.UtcNow : entry.LoggedAt);
            SetParameter(command, "@actor_user_name", entry.Actor ?? string.Empty);
            SetParameter(command, "@entity_type", entry.EntityType ?? string.Empty);
            SetParameter(command, "@entity_id", ToNullableId(entry.EntityId));
            SetParameter(command, "@entity_number", entry.EntityNumber ?? string.Empty);
            SetParameter(command, "@action_text", entry.Action ?? string.Empty);
            SetParameter(command, "@result_text", entry.Result ?? string.Empty);
            SetParameter(command, "@message_text", entry.Message ?? string.Empty);
            command.ExecuteNonQuery();
        }
    }

    private const string AppCatalogSchemaSql = """
        CREATE TABLE IF NOT EXISTS app_catalog_lists (
            list_kind VARCHAR(32) NOT NULL,
            value_text VARCHAR(256) NOT NULL,
            sort_order INT UNSIGNED NOT NULL DEFAULT 0,
            CONSTRAINT pk_app_catalog_lists PRIMARY KEY (list_kind, value_text)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_items (
            id CHAR(36) NOT NULL,
            code VARCHAR(128) NOT NULL,
            name VARCHAR(512) NOT NULL,
            unit_name VARCHAR(64) NULL,
            category_name VARCHAR(256) NULL,
            supplier_name VARCHAR(512) NULL,
            default_warehouse VARCHAR(256) NULL,
            status_text VARCHAR(128) NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            default_price DECIMAL(18, 4) NOT NULL DEFAULT 0,
            barcode_value VARCHAR(256) NULL,
            barcode_format VARCHAR(64) NULL,
            qr_payload TEXT NULL,
            notes TEXT NULL,
            source_label VARCHAR(256) NULL,
            item_type VARCHAR(64) NOT NULL DEFAULT 'Запас',
            name_for_print VARCHAR(512) NULL,
            parent_group VARCHAR(256) NULL,
            brand VARCHAR(256) NULL,
            color VARCHAR(64) NULL,
            description_text TEXT NULL,
            width_cm DECIMAL(12, 4) NOT NULL DEFAULT 0,
            height_cm DECIMAL(12, 4) NOT NULL DEFAULT 0,
            depth_cm DECIMAL(12, 4) NOT NULL DEFAULT 0,
            weight_kg DECIMAL(12, 4) NOT NULL DEFAULT 0,
            is_weight TINYINT(1) NOT NULL DEFAULT 0,
            forbid_fractional TINYINT(1) NOT NULL DEFAULT 0,
            min_sale_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            pack_quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            site_upload_status VARCHAR(64) NULL,
            is_inactive TINYINT(1) NOT NULL DEFAULT 0,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_catalog_items PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_product_barcodes (
            id BIGINT NOT NULL AUTO_INCREMENT,
            item_code VARCHAR(128) NOT NULL,
            barcode_value VARCHAR(256) NOT NULL,
            barcode_kind VARCHAR(32) NOT NULL DEFAULT 'Основной',
            barcode_source VARCHAR(64) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_product_barcodes PRIMARY KEY (id),
            CONSTRAINT uq_app_product_barcodes UNIQUE KEY (item_code, barcode_value),
            INDEX ix_app_product_barcodes_item_code (item_code),
            INDEX ix_app_product_barcodes_value (barcode_value)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_item_prices (
            id CHAR(36) NOT NULL,
            item_id CHAR(36) NOT NULL,
            price_type_id CHAR(36) NOT NULL,
            price_type_name VARCHAR(256) NULL,
            price_value DECIMAL(18, 4) NOT NULL DEFAULT 0,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_catalog_item_prices PRIMARY KEY (id),
            CONSTRAINT uq_app_catalog_item_prices_pair UNIQUE (item_id, price_type_id),
            CONSTRAINT fk_app_catalog_item_prices_item FOREIGN KEY (item_id) REFERENCES app_catalog_items (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_price_types (
            id CHAR(36) NOT NULL,
            code VARCHAR(128) NOT NULL,
            name VARCHAR(256) NOT NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            base_price_type_name VARCHAR(256) NULL,
            rounding_rule VARCHAR(256) NULL,
            is_default TINYINT(1) NOT NULL DEFAULT 0,
            is_manual_entry_only TINYINT(1) NOT NULL DEFAULT 0,
            uses_psychological_rounding TINYINT(1) NOT NULL DEFAULT 0,
            status_text VARCHAR(128) NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_catalog_price_types PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_discounts (
            id CHAR(36) NOT NULL,
            name VARCHAR(256) NOT NULL,
            percent_value DECIMAL(9, 4) NOT NULL DEFAULT 0,
            price_type_name VARCHAR(256) NULL,
            period_text VARCHAR(128) NULL,
            scope_text VARCHAR(256) NULL,
            status_text VARCHAR(128) NULL,
            comment_text TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_catalog_discounts PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_price_registrations (
            id CHAR(36) NOT NULL,
            number VARCHAR(128) NOT NULL,
            document_date DATETIME(6) NOT NULL,
            price_type_name VARCHAR(256) NULL,
            currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',
            status_text VARCHAR(128) NULL,
            comment_text TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_catalog_price_registrations PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_price_registration_lines (
            id CHAR(36) NOT NULL,
            registration_id CHAR(36) NOT NULL,
            line_no INT UNSIGNED NOT NULL,
            item_code VARCHAR(128) NULL,
            item_name VARCHAR(512) NULL,
            unit_name VARCHAR(64) NULL,
            previous_price DECIMAL(18, 4) NOT NULL DEFAULT 0,
            new_price DECIMAL(18, 4) NOT NULL DEFAULT 0,
            CONSTRAINT pk_app_catalog_price_registration_lines PRIMARY KEY (id),
            CONSTRAINT uq_app_catalog_price_registration_lines_line UNIQUE (registration_id, line_no),
            CONSTRAINT fk_app_catalog_price_registration_lines_document FOREIGN KEY (registration_id) REFERENCES app_catalog_price_registrations (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_catalog_operation_log (
            id CHAR(36) NOT NULL,
            logged_at DATETIME(6) NOT NULL,
            actor_user_name VARCHAR(128) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NULL,
            entity_number VARCHAR(128) NULL,
            action_text VARCHAR(256) NOT NULL,
            result_text VARCHAR(128) NOT NULL,
            message_text TEXT NULL,
            CONSTRAINT pk_app_catalog_operation_log PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        """;

    /// <summary>
    /// Загружает все штрихкоды по коду товара (item_code = НФ-XXXXXXXX).
    /// Если в новой таблице пусто, но в app_catalog_items есть barcode_value — возвращает один основной штрихкод как fallback.
    /// При ошибке БД пишет error.log и возвращает пустой список.
    /// </summary>
    internal IReadOnlyList<ProductBarcodeRecord> TryLoadProductBarcodes(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            return Array.Empty<ProductBarcodeRecord>();
        }

        try
        {
            EnsureDatabaseAndSchema();
            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlCatalogCommandTimeoutSeconds);

            var rows = new List<ProductBarcodeRecord>();
            using (var command = CreateMySqlCommand(connection, null, """
                SELECT id, item_code, barcode_value, barcode_kind, barcode_source, created_at_utc
                FROM app_product_barcodes
                WHERE item_code = @code
                ORDER BY CASE WHEN barcode_kind = 'Основной' THEN 0 ELSE 1 END, id;
                """))
            {
                SetParameter(command, "@code", itemCode);
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    rows.Add(new ProductBarcodeRecord
                    {
                        Id = ReadInt64(reader, "id"),
                        ItemCode = ReadString(reader, "item_code"),
                        BarcodeValue = ReadString(reader, "barcode_value"),
                        BarcodeKind = ReadString(reader, "barcode_kind"),
                        BarcodeSource = ReadString(reader, "barcode_source"),
                        CreatedAtUtc = ReadDateTime(reader, "created_at_utc")
                    });
                }
            }

            if (rows.Count == 0)
            {
                // Fallback: единственный штрихкод из app_catalog_items.barcode_value
                using var command = CreateMySqlCommand(connection, null, """
                    SELECT barcode_value, barcode_format
                    FROM app_catalog_items
                    WHERE code = @code AND barcode_value IS NOT NULL AND barcode_value <> ''
                    LIMIT 1;
                    """);
                SetParameter(command, "@code", itemCode);
                using var reader = command.ExecuteReader();
                if (reader.Read())
                {
                    rows.Add(new ProductBarcodeRecord
                    {
                        ItemCode = itemCode,
                        BarcodeValue = ReadString(reader, "barcode_value"),
                        BarcodeKind = "Основной",
                        BarcodeSource = ReadString(reader, "barcode_format"),
                        CreatedAtUtc = DateTime.UtcNow
                    });
                }
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<ProductBarcodeRecord>();
        }
    }

    private static long ReadInt64(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? 0L : reader.GetInt64(ordinal);
    }
}
