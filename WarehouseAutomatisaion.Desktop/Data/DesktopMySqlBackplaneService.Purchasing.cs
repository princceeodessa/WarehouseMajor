using System.ComponentModel;
using System.Text.Json;
using MySqlConnector;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed partial class DesktopMySqlBackplaneService
{
    private const string PurchasingModuleCode = "purchasing";
    private const int MysqlPurchasingCommandTimeoutSeconds = 90;

    internal DesktopModuleSnapshotRecord<PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot>? TryLoadPurchasingWorkspaceSnapshotRecord()
    {
        try
        {
            EnsureDatabaseAndSchema();
            var metadata = LoadPurchasingWorkspaceStateMetadata();
            if (metadata is null)
            {
                return null;
            }

            var snapshot = LoadPurchasingWorkspaceSnapshotRows();
            snapshot.CurrentOperator = metadata.UpdatedBy;
            return new DesktopModuleSnapshotRecord<PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot>(snapshot, metadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    internal DesktopModuleSnapshotSaveResult TrySavePurchasingWorkspaceSnapshot(
        PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            var metadata = SavePurchasingWorkspaceSnapshot(snapshot, actorName, expectedMetadata, auditEvents);
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

    private DesktopModuleSnapshotMetadata SavePurchasingWorkspaceSnapshot(
        PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var moduleCode = NormalizeModuleCode(PurchasingModuleCode);
        var actor = NormalizeUserName(actorName);
        var payloadHash = ComputeSha256(JsonSerializer.Serialize(snapshot, JsonOptions));

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlPurchasingCommandTimeoutSeconds);
        using var transaction = connection.BeginTransaction();

        var currentMetadata = LoadPurchasingWorkspaceStateMetadata(connection, transaction);
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
                    "Purchasing workspace rows were changed by another client.",
                    currentMetadata);
            }
        }
        else if (currentMetadata is null
                 || currentMetadata.VersionNo != expectedMetadata.VersionNo
                 || !string.Equals(currentMetadata.PayloadHash, expectedMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopModuleSnapshotConflictException(
                "Purchasing workspace rows were changed by another client.",
                currentMetadata);
        }

        ReplacePurchasingWorkspaceRows(connection, transaction, snapshot);

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

        return LoadPurchasingWorkspaceStateMetadata()
               ?? new DesktopModuleSnapshotMetadata(moduleCode, nextVersionNo, payloadHash, actor, DateTime.UtcNow);
    }

    private PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot LoadPurchasingWorkspaceSnapshotRows()
    {
        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlPurchasingCommandTimeoutSeconds);

        return new PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot
        {
            Suppliers = LoadPurchasingSuppliers(connection),
            PurchaseOrders = LoadPurchasingDocuments(connection, "purchase_order"),
            SupplierInvoices = LoadPurchasingDocuments(connection, "supplier_invoice"),
            PurchaseReceipts = LoadPurchasingDocuments(connection, "purchase_receipt"),
            OperationLog = LoadPurchasingOperationLog(connection)
        };
    }

    private DesktopModuleSnapshotMetadata? LoadPurchasingWorkspaceStateMetadata()
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
            WHERE module_code = {SqlUtf8TextExpression(NormalizeModuleCode(PurchasingModuleCode))}
            LIMIT 1;
            """;

        var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlPurchasingCommandTimeoutSeconds).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
        return row is null ? null : CreateSnapshotMetadata(PurchasingModuleCode, row);
    }

    private DesktopModuleSnapshotMetadata? LoadPurchasingWorkspaceStateMetadata(
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
        AddParameter(command, "@module_code", NormalizeModuleCode(PurchasingModuleCode));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DesktopModuleSnapshotMetadata(
            NormalizeModuleCode(PurchasingModuleCode),
            reader.GetInt32(reader.GetOrdinal("version_no")),
            ReadString(reader, "payload_hash"),
            ReadString(reader, "updated_by"),
            DateTime.SpecifyKind(ReadDateTime(reader, "updated_at_utc"), DateTimeKind.Utc));
    }

    private void ReplacePurchasingWorkspaceRows(
        MySqlConnection connection,
        MySqlTransaction transaction,
        PurchasingOperationalWorkspaceStore.PurchasingWorkspaceSnapshot snapshot)
    {
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_purchasing_operation_log;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_purchasing_document_lines;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_purchasing_documents;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_purchasing_suppliers;");

        InsertPurchasingSuppliers(connection, transaction, snapshot.Suppliers ?? []);
        InsertPurchasingDocuments(connection, transaction, "purchase_order", snapshot.PurchaseOrders ?? []);
        InsertPurchasingDocuments(connection, transaction, "supplier_invoice", snapshot.SupplierInvoices ?? []);
        InsertPurchasingDocuments(connection, transaction, "purchase_receipt", snapshot.PurchaseReceipts ?? []);
        InsertPurchasingOperationLog(connection, transaction, snapshot.OperationLog ?? []);
    }

    private static List<OperationalPurchasingSupplierRecord> LoadPurchasingSuppliers(MySqlConnection connection)
    {
        var suppliers = new List<OperationalPurchasingSupplierRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                code,
                name,
                status_text,
                tax_id,
                phone,
                email,
                contract_text,
                source_label,
                fields_json
            FROM app_purchasing_suppliers
            ORDER BY name, code;
            """);

        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            suppliers.Add(new OperationalPurchasingSupplierRecord
            {
                Id = ReadGuid(reader, "id"),
                Code = ReadString(reader, "code"),
                Name = ReadString(reader, "name"),
                Status = ReadString(reader, "status_text"),
                TaxId = ReadString(reader, "tax_id"),
                Phone = ReadString(reader, "phone"),
                Email = ReadString(reader, "email"),
                Contract = ReadString(reader, "contract_text"),
                SourceLabel = ReadString(reader, "source_label"),
                Fields = DeserializePurchasingFields(ReadString(reader, "fields_json"))
            });
        }

        return suppliers;
    }

    private static List<OperationalPurchasingDocumentRecord> LoadPurchasingDocuments(
        MySqlConnection connection,
        string documentKind)
    {
        var documents = new List<OperationalPurchasingDocumentRecord>();
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                document_type,
                number,
                document_date,
                due_date,
                supplier_id,
                supplier_name,
                status_text,
                contract_text,
                warehouse_name,
                related_order_id,
                related_order_number,
                comment_text,
                source_label,
                fields_json
            FROM app_purchasing_documents
            WHERE document_kind = @document_kind
            ORDER BY document_date DESC, number DESC;
            """))
        {
            AddParameter(command, "@document_kind", documentKind);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents.Add(new OperationalPurchasingDocumentRecord
                {
                    Id = ReadGuid(reader, "id"),
                    DocumentType = ReadString(reader, "document_type"),
                    Number = ReadString(reader, "number"),
                    DocumentDate = ReadDateTime(reader, "document_date"),
                    DueDate = ReadNullableDateTime(reader, "due_date"),
                    SupplierId = ReadGuid(reader, "supplier_id"),
                    SupplierName = ReadString(reader, "supplier_name"),
                    Status = ReadString(reader, "status_text"),
                    Contract = ReadString(reader, "contract_text"),
                    Warehouse = ReadString(reader, "warehouse_name"),
                    RelatedOrderId = ReadGuid(reader, "related_order_id"),
                    RelatedOrderNumber = ReadString(reader, "related_order_number"),
                    Comment = ReadString(reader, "comment_text"),
                    SourceLabel = ReadString(reader, "source_label"),
                    Fields = DeserializePurchasingFields(ReadString(reader, "fields_json")),
                    Lines = new BindingList<OperationalPurchasingLineRecord>()
                });
            }
        }

        var byId = documents.ToDictionary(item => item.Id);
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                document_id,
                id,
                section_name,
                item_code,
                item_name,
                quantity,
                unit_name,
                price,
                planned_date,
                target_location,
                related_document,
                fields_json
            FROM app_purchasing_document_lines
            WHERE document_kind = @document_kind
            ORDER BY document_id, line_no;
            """))
        {
            AddParameter(command, "@document_kind", documentKind);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var documentId = ReadGuid(reader, "document_id");
                if (!byId.TryGetValue(documentId, out var document))
                {
                    continue;
                }

                document.Lines.Add(new OperationalPurchasingLineRecord
                {
                    Id = ReadGuid(reader, "id"),
                    SectionName = ReadString(reader, "section_name"),
                    ItemCode = ReadString(reader, "item_code"),
                    ItemName = ReadString(reader, "item_name"),
                    Quantity = ReadDecimal(reader, "quantity"),
                    Unit = ReadString(reader, "unit_name"),
                    Price = ReadDecimal(reader, "price"),
                    PlannedDate = ReadNullableDateTime(reader, "planned_date"),
                    TargetLocation = ReadString(reader, "target_location"),
                    RelatedDocument = ReadString(reader, "related_document"),
                    Fields = DeserializePurchasingFields(ReadString(reader, "fields_json"))
                });
            }
        }

        return documents;
    }

    private static List<PurchasingOperationLogEntry> LoadPurchasingOperationLog(MySqlConnection connection)
    {
        var entries = new List<PurchasingOperationLogEntry>();
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
            FROM app_purchasing_operation_log
            ORDER BY logged_at DESC
            LIMIT 500;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new PurchasingOperationLogEntry
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

    private static void InsertPurchasingSuppliers(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<OperationalPurchasingSupplierRecord> suppliers)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_purchasing_suppliers (
                id,
                code,
                name,
                status_text,
                tax_id,
                phone,
                email,
                contract_text,
                source_label,
                fields_json
            )
            VALUES (
                @id,
                @code,
                @name,
                @status_text,
                @tax_id,
                @phone,
                @email,
                @contract_text,
                @source_label,
                @fields_json
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@code", "@name", "@status_text", "@tax_id",
                     "@phone", "@email", "@contract_text", "@source_label", "@fields_json"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var supplier in suppliers)
        {
            var supplierId = EnsureId(supplier.Id, $"purchasing-supplier|{supplier.Code}|{supplier.Name}");
            SetParameter(command, "@id", supplierId.ToString());
            SetParameter(command, "@code", supplier.Code ?? string.Empty);
            SetParameter(command, "@name", supplier.Name ?? string.Empty);
            SetParameter(command, "@status_text", supplier.Status ?? string.Empty);
            SetParameter(command, "@tax_id", supplier.TaxId ?? string.Empty);
            SetParameter(command, "@phone", supplier.Phone ?? string.Empty);
            SetParameter(command, "@email", supplier.Email ?? string.Empty);
            SetParameter(command, "@contract_text", supplier.Contract ?? string.Empty);
            SetParameter(command, "@source_label", supplier.SourceLabel ?? string.Empty);
            SetParameter(command, "@fields_json", SerializePurchasingFields(supplier.Fields));
            command.ExecuteNonQuery();
        }
    }

    private static void InsertPurchasingDocuments(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string documentKind,
        IEnumerable<OperationalPurchasingDocumentRecord> documents)
    {
        using var documentCommand = CreatePurchasingDocumentCommand(connection, transaction);
        using var lineCommand = CreatePurchasingDocumentLineCommand(connection, transaction);

        foreach (var document in documents)
        {
            var documentId = EnsureId(document.Id, $"purchasing-{documentKind}|{document.Number}|{document.DocumentDate:O}");
            SetParameter(documentCommand, "@id", documentId.ToString());
            SetParameter(documentCommand, "@document_kind", documentKind);
            SetParameter(documentCommand, "@document_type", document.DocumentType ?? string.Empty);
            SetParameter(documentCommand, "@number", document.Number ?? string.Empty);
            SetParameter(documentCommand, "@document_date", document.DocumentDate == default ? DateTime.Today : document.DocumentDate);
            SetParameter(documentCommand, "@due_date", document.DueDate);
            SetParameter(documentCommand, "@supplier_id", ToNullableId(document.SupplierId));
            SetParameter(documentCommand, "@supplier_name", document.SupplierName ?? string.Empty);
            SetParameter(documentCommand, "@status_text", document.Status ?? string.Empty);
            SetParameter(documentCommand, "@contract_text", document.Contract ?? string.Empty);
            SetParameter(documentCommand, "@warehouse_name", document.Warehouse ?? string.Empty);
            SetParameter(documentCommand, "@related_order_id", ToNullableId(document.RelatedOrderId));
            SetParameter(documentCommand, "@related_order_number", document.RelatedOrderNumber ?? string.Empty);
            SetParameter(documentCommand, "@comment_text", document.Comment ?? string.Empty);
            SetParameter(documentCommand, "@source_label", document.SourceLabel ?? string.Empty);
            SetParameter(documentCommand, "@fields_json", SerializePurchasingFields(document.Fields));
            documentCommand.ExecuteNonQuery();

            var lineNo = 1;
            foreach (var line in document.Lines ?? [])
            {
                var lineId = EnsureId(line.Id, $"{documentId:N}|purchasing-line|{lineNo}");
                SetParameter(lineCommand, "@id", lineId.ToString());
                SetParameter(lineCommand, "@document_id", documentId.ToString());
                SetParameter(lineCommand, "@document_kind", documentKind);
                SetParameter(lineCommand, "@line_no", lineNo);
                SetParameter(lineCommand, "@section_name", line.SectionName ?? string.Empty);
                SetParameter(lineCommand, "@item_code", line.ItemCode ?? string.Empty);
                SetParameter(lineCommand, "@item_name", line.ItemName ?? string.Empty);
                SetParameter(lineCommand, "@quantity", line.Quantity);
                SetParameter(lineCommand, "@unit_name", line.Unit ?? string.Empty);
                SetParameter(lineCommand, "@price", line.Price);
                SetParameter(lineCommand, "@planned_date", line.PlannedDate);
                SetParameter(lineCommand, "@target_location", line.TargetLocation ?? string.Empty);
                SetParameter(lineCommand, "@related_document", line.RelatedDocument ?? string.Empty);
                SetParameter(lineCommand, "@fields_json", SerializePurchasingFields(line.Fields));
                lineCommand.ExecuteNonQuery();
                lineNo++;
            }
        }
    }

    private static MySqlCommand CreatePurchasingDocumentCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_purchasing_documents (
                id,
                document_kind,
                document_type,
                number,
                document_date,
                due_date,
                supplier_id,
                supplier_name,
                status_text,
                contract_text,
                warehouse_name,
                related_order_id,
                related_order_number,
                comment_text,
                source_label,
                fields_json
            )
            VALUES (
                @id,
                @document_kind,
                @document_type,
                @number,
                @document_date,
                @due_date,
                @supplier_id,
                @supplier_name,
                @status_text,
                @contract_text,
                @warehouse_name,
                @related_order_id,
                @related_order_number,
                @comment_text,
                @source_label,
                @fields_json
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@document_kind", "@document_type", "@number", "@document_date",
                     "@due_date", "@supplier_id", "@supplier_name", "@status_text", "@contract_text",
                     "@warehouse_name", "@related_order_id", "@related_order_number", "@comment_text",
                     "@source_label", "@fields_json"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static MySqlCommand CreatePurchasingDocumentLineCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_purchasing_document_lines (
                id,
                document_id,
                document_kind,
                line_no,
                section_name,
                item_code,
                item_name,
                quantity,
                unit_name,
                price,
                planned_date,
                target_location,
                related_document,
                fields_json
            )
            VALUES (
                @id,
                @document_id,
                @document_kind,
                @line_no,
                @section_name,
                @item_code,
                @item_name,
                @quantity,
                @unit_name,
                @price,
                @planned_date,
                @target_location,
                @related_document,
                @fields_json
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@document_id", "@document_kind", "@line_no", "@section_name",
                     "@item_code", "@item_name", "@quantity", "@unit_name", "@price",
                     "@planned_date", "@target_location", "@related_document", "@fields_json"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static void InsertPurchasingOperationLog(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<PurchasingOperationLogEntry> operationLog)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_purchasing_operation_log (
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
            );
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
            var entryId = EnsureId(entry.Id, $"purchasing-log|{entry.EntityNumber}|{entry.Action}|{entry.LoggedAt:O}");
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

    private static IReadOnlyList<OneCFieldValue> DeserializePurchasingFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<OneCFieldValue>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<OneCFieldValue>>(json, JsonOptions)?.ToArray()
                   ?? Array.Empty<OneCFieldValue>();
        }
        catch
        {
            return Array.Empty<OneCFieldValue>();
        }
    }

    private static string SerializePurchasingFields(IReadOnlyList<OneCFieldValue>? fields)
    {
        return JsonSerializer.Serialize(fields ?? Array.Empty<OneCFieldValue>(), JsonOptions);
    }

    private const string AppPurchasingSchemaSql = """
        CREATE TABLE IF NOT EXISTS app_purchasing_suppliers (
            id CHAR(36) NOT NULL,
            code VARCHAR(128) NOT NULL,
            name VARCHAR(512) NOT NULL,
            status_text VARCHAR(128) NULL,
            tax_id VARCHAR(64) NULL,
            phone VARCHAR(128) NULL,
            email VARCHAR(256) NULL,
            contract_text VARCHAR(256) NULL,
            source_label VARCHAR(256) NULL,
            fields_json JSON NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_purchasing_suppliers PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_purchasing_documents (
            id CHAR(36) NOT NULL,
            document_kind VARCHAR(32) NOT NULL,
            document_type VARCHAR(128) NOT NULL,
            number VARCHAR(128) NOT NULL,
            document_date DATETIME(6) NOT NULL,
            due_date DATETIME(6) NULL,
            supplier_id CHAR(36) NULL,
            supplier_name VARCHAR(512) NULL,
            status_text VARCHAR(128) NULL,
            contract_text VARCHAR(256) NULL,
            warehouse_name VARCHAR(256) NULL,
            related_order_id CHAR(36) NULL,
            related_order_number VARCHAR(128) NULL,
            comment_text TEXT NULL,
            source_label VARCHAR(256) NULL,
            fields_json JSON NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_purchasing_documents PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_purchasing_document_lines (
            id CHAR(36) NOT NULL,
            document_id CHAR(36) NOT NULL,
            document_kind VARCHAR(32) NOT NULL,
            line_no INT UNSIGNED NOT NULL,
            section_name VARCHAR(256) NULL,
            item_code VARCHAR(128) NULL,
            item_name VARCHAR(512) NULL,
            quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            unit_name VARCHAR(64) NULL,
            price DECIMAL(18, 4) NOT NULL DEFAULT 0,
            planned_date DATETIME(6) NULL,
            target_location VARCHAR(256) NULL,
            related_document VARCHAR(256) NULL,
            fields_json JSON NULL,
            CONSTRAINT pk_app_purchasing_document_lines PRIMARY KEY (id),
            CONSTRAINT fk_app_purchasing_document_lines_document
                FOREIGN KEY (document_id) REFERENCES app_purchasing_documents (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_purchasing_operation_log (
            id CHAR(36) NOT NULL,
            logged_at DATETIME(6) NOT NULL,
            actor_user_name VARCHAR(128) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NULL,
            entity_number VARCHAR(128) NULL,
            action_text VARCHAR(256) NOT NULL,
            result_text VARCHAR(128) NOT NULL,
            message_text TEXT NULL,
            CONSTRAINT pk_app_purchasing_operation_log PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        """;
}
