using System.ComponentModel;
using System.Text.Json;
using MySqlConnector;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Data;

public sealed partial class DesktopMySqlBackplaneService
{
    private const string WarehouseModuleCode = "warehouse";
    private const int MysqlWarehouseCommandTimeoutSeconds = 90;

    internal DesktopModuleSnapshotRecord<WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot>? TryLoadWarehouseWorkspaceSnapshotRecord()
    {
        try
        {
            EnsureDatabaseAndSchema();
            var metadata = LoadWarehouseWorkspaceStateMetadata();
            if (metadata is null)
            {
                return null;
            }

            var snapshot = LoadWarehouseWorkspaceSnapshotRows();
            snapshot.CurrentOperator = metadata.UpdatedBy;
            return new DesktopModuleSnapshotRecord<WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot>(snapshot, metadata);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    internal DesktopModuleSnapshotSaveResult TrySaveWarehouseWorkspaceSnapshot(
        WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents = null)
    {
        try
        {
            var metadata = SaveWarehouseWorkspaceSnapshot(snapshot, actorName, expectedMetadata, auditEvents);
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

    private DesktopModuleSnapshotMetadata SaveWarehouseWorkspaceSnapshot(
        WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot snapshot,
        string actorName,
        DesktopModuleSnapshotMetadata? expectedMetadata,
        IEnumerable<DesktopAuditEventSeed>? auditEvents)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(actorName);

        var moduleCode = NormalizeModuleCode(WarehouseModuleCode);
        var actor = NormalizeUserName(actorName);
        var payloadHash = ComputeSha256(JsonSerializer.Serialize(snapshot, JsonOptions));

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlWarehouseCommandTimeoutSeconds);
        using var transaction = connection.BeginTransaction();

        var currentMetadata = LoadWarehouseWorkspaceStateMetadata(connection, transaction);
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
                    "Warehouse workspace rows were changed by another client.",
                    currentMetadata);
            }
        }
        else if (currentMetadata is null
                 || currentMetadata.VersionNo != expectedMetadata.VersionNo
                 || !string.Equals(currentMetadata.PayloadHash, expectedMetadata.PayloadHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new DesktopModuleSnapshotConflictException(
                "Warehouse workspace rows were changed by another client.",
                currentMetadata);
        }

        ReplaceWarehouseWorkspaceRows(connection, transaction, snapshot);

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

        return LoadWarehouseWorkspaceStateMetadata()
               ?? new DesktopModuleSnapshotMetadata(moduleCode, nextVersionNo, payloadHash, actor, DateTime.UtcNow);
    }

    private WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot LoadWarehouseWorkspaceSnapshotRows()
    {
        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlWarehouseCommandTimeoutSeconds);

        return new WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot
        {
            TransferOrders = LoadWarehouseDocuments(connection, "transfer"),
            InventoryCounts = LoadWarehouseDocuments(connection, "inventory"),
            WriteOffs = LoadWarehouseDocuments(connection, "write_off"),
            StorageCells = LoadWarehouseStorageCells(connection),
            OperationLog = LoadWarehouseOperationLog(connection)
        };
    }

    private DesktopModuleSnapshotMetadata? LoadWarehouseWorkspaceStateMetadata()
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
            WHERE module_code = {SqlUtf8TextExpression(NormalizeModuleCode(WarehouseModuleCode))}
            LIMIT 1;
            """;

        var output = ExecuteSqlScalar(sql, useDatabase: true, commandTimeoutSeconds: MysqlWarehouseCommandTimeoutSeconds).Trim();
        if (string.IsNullOrWhiteSpace(output) || string.Equals(output, "NULL", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var row = JsonSerializer.Deserialize<DesktopModuleSnapshotRow>(output, JsonOptions);
        return row is null ? null : CreateSnapshotMetadata(WarehouseModuleCode, row);
    }

    private DesktopModuleSnapshotMetadata? LoadWarehouseWorkspaceStateMetadata(
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
        AddParameter(command, "@module_code", NormalizeModuleCode(WarehouseModuleCode));

        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new DesktopModuleSnapshotMetadata(
            NormalizeModuleCode(WarehouseModuleCode),
            reader.GetInt32(reader.GetOrdinal("version_no")),
            ReadString(reader, "payload_hash"),
            ReadString(reader, "updated_by"),
            DateTime.SpecifyKind(ReadDateTime(reader, "updated_at_utc"), DateTimeKind.Utc));
    }

    private void ReplaceWarehouseWorkspaceRows(
        MySqlConnection connection,
        MySqlTransaction transaction,
        WarehouseOperationalWorkspaceStore.WarehouseWorkspaceSnapshot snapshot)
    {
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_warehouse_operation_log;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_warehouse_storage_cells;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_warehouse_document_lines;");
        ExecuteMySqlNonQuery(connection, transaction, "DELETE FROM app_warehouse_documents;");

        InsertWarehouseDocuments(connection, transaction, "transfer", snapshot.TransferOrders ?? []);
        InsertWarehouseDocuments(connection, transaction, "inventory", snapshot.InventoryCounts ?? []);
        InsertWarehouseDocuments(connection, transaction, "write_off", snapshot.WriteOffs ?? []);
        InsertWarehouseStorageCells(connection, transaction, snapshot.StorageCells ?? []);
        InsertWarehouseOperationLog(connection, transaction, snapshot.OperationLog ?? []);
    }

    private static List<OperationalWarehouseDocumentRecord> LoadWarehouseDocuments(
        MySqlConnection connection,
        string documentKind)
    {
        var documents = new List<OperationalWarehouseDocumentRecord>();
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                document_type,
                number,
                document_date,
                status_text,
                source_warehouse,
                target_warehouse,
                related_document,
                comment_text,
                source_label,
                fields_json
            FROM app_warehouse_documents
            WHERE document_kind = @document_kind
            ORDER BY document_date DESC, number DESC;
            """))
        {
            AddParameter(command, "@document_kind", documentKind);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                documents.Add(new OperationalWarehouseDocumentRecord
                {
                    Id = ReadGuid(reader, "id"),
                    DocumentType = ReadString(reader, "document_type"),
                    Number = ReadString(reader, "number"),
                    DocumentDate = ReadDateTime(reader, "document_date"),
                    Status = ReadString(reader, "status_text"),
                    SourceWarehouse = ReadString(reader, "source_warehouse"),
                    TargetWarehouse = ReadString(reader, "target_warehouse"),
                    RelatedDocument = ReadString(reader, "related_document"),
                    Comment = ReadString(reader, "comment_text"),
                    SourceLabel = ReadString(reader, "source_label"),
                    Fields = DeserializeWarehouseFields(ReadString(reader, "fields_json")),
                    Lines = new BindingList<OperationalWarehouseLineRecord>()
                });
            }
        }

        var byId = documents.ToDictionary(item => item.Id);
        using (var command = CreateMySqlCommand(connection, null, """
            SELECT
                document_id,
                id,
                item_code,
                item_name,
                quantity,
                unit_name,
                source_location,
                target_location,
                related_document,
                fields_json
            FROM app_warehouse_document_lines
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

                document.Lines.Add(new OperationalWarehouseLineRecord
                {
                    Id = ReadGuid(reader, "id"),
                    ItemCode = ReadString(reader, "item_code"),
                    ItemName = ReadString(reader, "item_name"),
                    Quantity = ReadDecimal(reader, "quantity"),
                    Unit = ReadString(reader, "unit_name"),
                    SourceLocation = ReadString(reader, "source_location"),
                    TargetLocation = ReadString(reader, "target_location"),
                    RelatedDocument = ReadString(reader, "related_document"),
                    Fields = DeserializeWarehouseFields(ReadString(reader, "fields_json"))
                });
            }
        }

        return documents;
    }

    private static List<WarehouseStorageCellRecord> LoadWarehouseStorageCells(MySqlConnection connection)
    {
        var cells = new List<WarehouseStorageCellRecord>();
        using var command = CreateMySqlCommand(connection, null, """
            SELECT
                id,
                warehouse_name,
                code,
                zone_code,
                zone_name,
                row_no,
                rack_no,
                shelf_no,
                cell_no,
                cell_type,
                capacity,
                status_text,
                qr_payload,
                comment_text
            FROM app_warehouse_storage_cells
            ORDER BY warehouse_name, code;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            cells.Add(new WarehouseStorageCellRecord
            {
                Id = ReadGuid(reader, "id"),
                Warehouse = ReadString(reader, "warehouse_name"),
                Code = ReadString(reader, "code"),
                ZoneCode = ReadString(reader, "zone_code"),
                ZoneName = ReadString(reader, "zone_name"),
                Row = ReadInt32(reader, "row_no"),
                Rack = ReadInt32(reader, "rack_no"),
                Shelf = ReadInt32(reader, "shelf_no"),
                Cell = ReadInt32(reader, "cell_no"),
                CellType = ReadString(reader, "cell_type"),
                Capacity = ReadDecimal(reader, "capacity"),
                Status = ReadString(reader, "status_text"),
                QrPayload = ReadString(reader, "qr_payload"),
                Comment = ReadString(reader, "comment_text")
            });
        }

        return cells;
    }

    private static List<WarehouseOperationLogEntry> LoadWarehouseOperationLog(MySqlConnection connection)
    {
        var entries = new List<WarehouseOperationLogEntry>();
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
            FROM app_warehouse_operation_log
            ORDER BY logged_at DESC
            LIMIT 500;
            """);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new WarehouseOperationLogEntry
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

    private static void InsertWarehouseDocuments(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string documentKind,
        IEnumerable<OperationalWarehouseDocumentRecord> documents)
    {
        using var documentCommand = CreateWarehouseDocumentCommand(connection, transaction);
        using var lineCommand = CreateWarehouseDocumentLineCommand(connection, transaction);

        foreach (var document in documents)
        {
            var documentId = EnsureId(document.Id, $"warehouse-{documentKind}|{document.Number}|{document.DocumentDate:O}");
            SetParameter(documentCommand, "@id", documentId.ToString());
            SetParameter(documentCommand, "@document_kind", documentKind);
            SetParameter(documentCommand, "@document_type", document.DocumentType ?? string.Empty);
            SetParameter(documentCommand, "@number", document.Number ?? string.Empty);
            SetParameter(documentCommand, "@document_date", document.DocumentDate == default ? DateTime.Today : document.DocumentDate);
            SetParameter(documentCommand, "@status_text", document.Status ?? string.Empty);
            SetParameter(documentCommand, "@source_warehouse", document.SourceWarehouse ?? string.Empty);
            SetParameter(documentCommand, "@target_warehouse", document.TargetWarehouse ?? string.Empty);
            SetParameter(documentCommand, "@related_document", document.RelatedDocument ?? string.Empty);
            SetParameter(documentCommand, "@comment_text", document.Comment ?? string.Empty);
            SetParameter(documentCommand, "@source_label", document.SourceLabel ?? string.Empty);
            SetParameter(documentCommand, "@fields_json", SerializeWarehouseFields(document.Fields));
            documentCommand.ExecuteNonQuery();

            var lineNo = 1;
            foreach (var line in document.Lines ?? [])
            {
                var lineId = EnsureId(line.Id, $"{documentId:N}|warehouse-line|{lineNo}");
                SetParameter(lineCommand, "@id", lineId.ToString());
                SetParameter(lineCommand, "@document_id", documentId.ToString());
                SetParameter(lineCommand, "@document_kind", documentKind);
                SetParameter(lineCommand, "@line_no", lineNo);
                SetParameter(lineCommand, "@item_code", line.ItemCode ?? string.Empty);
                SetParameter(lineCommand, "@item_name", line.ItemName ?? string.Empty);
                SetParameter(lineCommand, "@quantity", line.Quantity);
                SetParameter(lineCommand, "@unit_name", line.Unit ?? string.Empty);
                SetParameter(lineCommand, "@source_location", line.SourceLocation ?? string.Empty);
                SetParameter(lineCommand, "@target_location", line.TargetLocation ?? string.Empty);
                SetParameter(lineCommand, "@related_document", line.RelatedDocument ?? string.Empty);
                SetParameter(lineCommand, "@fields_json", SerializeWarehouseFields(line.Fields));
                lineCommand.ExecuteNonQuery();
                lineNo++;
            }
        }
    }

    private static MySqlCommand CreateWarehouseDocumentCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_warehouse_documents (
                id,
                document_kind,
                document_type,
                number,
                document_date,
                status_text,
                source_warehouse,
                target_warehouse,
                related_document,
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
                @status_text,
                @source_warehouse,
                @target_warehouse,
                @related_document,
                @comment_text,
                @source_label,
                @fields_json
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@document_kind", "@document_type", "@number", "@document_date",
                     "@status_text", "@source_warehouse", "@target_warehouse", "@related_document",
                     "@comment_text", "@source_label", "@fields_json"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static MySqlCommand CreateWarehouseDocumentLineCommand(
        MySqlConnection connection,
        MySqlTransaction transaction)
    {
        var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_warehouse_document_lines (
                id,
                document_id,
                document_kind,
                line_no,
                item_code,
                item_name,
                quantity,
                unit_name,
                source_location,
                target_location,
                related_document,
                fields_json
            )
            VALUES (
                @id,
                @document_id,
                @document_kind,
                @line_no,
                @item_code,
                @item_name,
                @quantity,
                @unit_name,
                @source_location,
                @target_location,
                @related_document,
                @fields_json
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@document_id", "@document_kind", "@line_no", "@item_code",
                     "@item_name", "@quantity", "@unit_name", "@source_location", "@target_location",
                     "@related_document", "@fields_json"
                 })
        {
            AddParameter(command, name);
        }

        return command;
    }

    private static void InsertWarehouseStorageCells(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<WarehouseStorageCellRecord> cells)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_warehouse_storage_cells (
                id,
                warehouse_name,
                code,
                zone_code,
                zone_name,
                row_no,
                rack_no,
                shelf_no,
                cell_no,
                cell_type,
                capacity,
                status_text,
                qr_payload,
                comment_text
            )
            VALUES (
                @id,
                @warehouse_name,
                @code,
                @zone_code,
                @zone_name,
                @row_no,
                @rack_no,
                @shelf_no,
                @cell_no,
                @cell_type,
                @capacity,
                @status_text,
                @qr_payload,
                @comment_text
            );
            """);
        foreach (var name in new[]
                 {
                     "@id", "@warehouse_name", "@code", "@zone_code", "@zone_name",
                     "@row_no", "@rack_no", "@shelf_no", "@cell_no", "@cell_type",
                     "@capacity", "@status_text", "@qr_payload", "@comment_text"
                 })
        {
            AddParameter(command, name);
        }

        foreach (var cell in cells)
        {
            var cellId = EnsureId(cell.Id, $"warehouse-cell|{cell.Warehouse}|{cell.Code}");
            SetParameter(command, "@id", cellId.ToString());
            SetParameter(command, "@warehouse_name", cell.Warehouse ?? string.Empty);
            SetParameter(command, "@code", cell.Code ?? string.Empty);
            SetParameter(command, "@zone_code", cell.ZoneCode ?? string.Empty);
            SetParameter(command, "@zone_name", cell.ZoneName ?? string.Empty);
            SetParameter(command, "@row_no", cell.Row);
            SetParameter(command, "@rack_no", cell.Rack);
            SetParameter(command, "@shelf_no", cell.Shelf);
            SetParameter(command, "@cell_no", cell.Cell);
            SetParameter(command, "@cell_type", cell.CellType ?? string.Empty);
            SetParameter(command, "@capacity", cell.Capacity);
            SetParameter(command, "@status_text", cell.Status ?? string.Empty);
            SetParameter(command, "@qr_payload", cell.QrPayload ?? string.Empty);
            SetParameter(command, "@comment_text", cell.Comment ?? string.Empty);
            command.ExecuteNonQuery();
        }
    }

    private static void InsertWarehouseOperationLog(
        MySqlConnection connection,
        MySqlTransaction transaction,
        IEnumerable<WarehouseOperationLogEntry> operationLog)
    {
        using var command = CreateMySqlCommand(connection, transaction, """
            INSERT INTO app_warehouse_operation_log (
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
            var entryId = EnsureId(entry.Id, $"warehouse-log|{entry.EntityNumber}|{entry.Action}|{entry.LoggedAt:O}");
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

    private static IReadOnlyList<OneCFieldValue> DeserializeWarehouseFields(string json)
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

    private static string SerializeWarehouseFields(IReadOnlyList<OneCFieldValue>? fields)
    {
        return JsonSerializer.Serialize(fields ?? Array.Empty<OneCFieldValue>(), JsonOptions);
    }

    private static int ReadInt32(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
    }

    private const string AppWarehouseSchemaSql = """
        CREATE TABLE IF NOT EXISTS app_warehouse_documents (
            id CHAR(36) NOT NULL,
            document_kind VARCHAR(32) NOT NULL,
            document_type VARCHAR(128) NOT NULL,
            number VARCHAR(128) NOT NULL,
            document_date DATETIME(6) NOT NULL,
            status_text VARCHAR(128) NULL,
            source_warehouse VARCHAR(256) NULL,
            target_warehouse VARCHAR(256) NULL,
            related_document VARCHAR(256) NULL,
            comment_text TEXT NULL,
            source_label VARCHAR(256) NULL,
            fields_json JSON NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_warehouse_documents PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_warehouse_document_lines (
            id CHAR(36) NOT NULL,
            document_id CHAR(36) NOT NULL,
            document_kind VARCHAR(32) NOT NULL,
            line_no INT UNSIGNED NOT NULL,
            item_code VARCHAR(128) NULL,
            item_name VARCHAR(512) NULL,
            quantity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            unit_name VARCHAR(64) NULL,
            source_location VARCHAR(256) NULL,
            target_location VARCHAR(256) NULL,
            related_document VARCHAR(256) NULL,
            fields_json JSON NULL,
            CONSTRAINT pk_app_warehouse_document_lines PRIMARY KEY (id),
            CONSTRAINT fk_app_warehouse_document_lines_document FOREIGN KEY (document_id) REFERENCES app_warehouse_documents (id) ON DELETE CASCADE
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_warehouse_storage_cells (
            id CHAR(36) NOT NULL,
            warehouse_name VARCHAR(256) NOT NULL,
            code VARCHAR(128) NOT NULL,
            zone_code VARCHAR(64) NULL,
            zone_name VARCHAR(256) NULL,
            row_no INT NOT NULL DEFAULT 0,
            rack_no INT NOT NULL DEFAULT 0,
            shelf_no INT NOT NULL DEFAULT 0,
            cell_no INT NOT NULL DEFAULT 0,
            cell_type VARCHAR(128) NULL,
            capacity DECIMAL(18, 4) NOT NULL DEFAULT 0,
            status_text VARCHAR(128) NULL,
            qr_payload TEXT NULL,
            comment_text TEXT NULL,
            created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
            updated_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6) ON UPDATE CURRENT_TIMESTAMP(6),
            CONSTRAINT pk_app_warehouse_storage_cells PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        CREATE TABLE IF NOT EXISTS app_warehouse_operation_log (
            id CHAR(36) NOT NULL,
            logged_at DATETIME(6) NOT NULL,
            actor_user_name VARCHAR(128) NOT NULL,
            entity_type VARCHAR(128) NOT NULL,
            entity_id CHAR(36) NULL,
            entity_number VARCHAR(128) NULL,
            action_text VARCHAR(256) NOT NULL,
            result_text VARCHAR(128) NOT NULL,
            message_text TEXT NULL,
            CONSTRAINT pk_app_warehouse_operation_log PRIMARY KEY (id)
        ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

        """;
}
