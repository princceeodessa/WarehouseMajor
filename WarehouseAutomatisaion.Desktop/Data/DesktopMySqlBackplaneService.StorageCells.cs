using MySqlConnector;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 3 Task 21: результат массового импорта ячеек.
public sealed record StorageCellBulkImportResult(
    int Inserted,
    int Updated,
    int Failed,
    IReadOnlyList<string> Errors);

// Sprint 3: CRUD для app_warehouse_storage_cells.
// Sql соответствует DDL из mysql-operational-schema.sql (без расширения схемы в этом спринте).
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlStorageCellsCommandTimeoutSeconds = 30;

    public IReadOnlyList<StorageCell> LoadStorageCells(string? warehouseFilter = null)
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStorageCellsCommandTimeoutSeconds);

            const string sql = """
                SELECT
                    id, warehouse_name, code,
                    zone_code, zone_name,
                    row_no, rack_no, shelf_no, cell_no,
                    cell_type, capacity, status_text,
                    qr_payload, comment_text,
                    created_at_utc, updated_at_utc
                FROM app_warehouse_storage_cells
                WHERE (@warehouse_filter IS NULL OR warehouse_name = @warehouse_filter)
                ORDER BY warehouse_name, zone_code, row_no, rack_no, shelf_no, cell_no, code;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@warehouse_filter",
                string.IsNullOrWhiteSpace(warehouseFilter) ? DBNull.Value : warehouseFilter);

            var rows = new List<StorageCell>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(ReadStorageCellRow(reader));
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<StorageCell>();
        }
    }

    public StorageCell? GetStorageCellById(Guid id)
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStorageCellsCommandTimeoutSeconds);

            const string sql = """
                SELECT
                    id, warehouse_name, code,
                    zone_code, zone_name,
                    row_no, rack_no, shelf_no, cell_no,
                    cell_type, capacity, status_text,
                    qr_payload, comment_text,
                    created_at_utc, updated_at_utc
                FROM app_warehouse_storage_cells
                WHERE id = @id
                LIMIT 1;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@id", id.ToString());

            using var reader = command.ExecuteReader();
            return reader.Read() ? ReadStorageCellRow(reader) : null;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public Guid CreateStorageCell(StorageCellRequest request)
    {
        EnsureDatabaseAndSchema();

        var id = Guid.NewGuid();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStorageCellsCommandTimeoutSeconds);

        const string sql = """
            INSERT INTO app_warehouse_storage_cells (
                id, warehouse_name, code,
                zone_code, zone_name,
                row_no, rack_no, shelf_no, cell_no,
                cell_type, capacity, status_text,
                qr_payload, comment_text,
                created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @warehouse_name, @code,
                @zone_code, @zone_name,
                @row_no, @rack_no, @shelf_no, @cell_no,
                @cell_type, @capacity, @status_text,
                @qr_payload, @comment_text,
                UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            );
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
        BindRequestParameters(command, id, request, includeQr: true);
        command.ExecuteNonQuery();

        return id;
    }

    public void UpdateStorageCell(Guid id, StorageCellRequest request)
    {
        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStorageCellsCommandTimeoutSeconds);

        const string sql = """
            UPDATE app_warehouse_storage_cells
            SET warehouse_name = @warehouse_name,
                code = @code,
                zone_code = @zone_code,
                zone_name = @zone_name,
                row_no = @row_no, rack_no = @rack_no,
                shelf_no = @shelf_no, cell_no = @cell_no,
                cell_type = @cell_type,
                capacity = @capacity,
                status_text = @status_text,
                comment_text = @comment_text,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
        BindRequestParameters(command, id, request, includeQr: false);
        command.ExecuteNonQuery();
    }

    public StorageCellBulkImportResult BulkUpsertStorageCells(IReadOnlyList<StorageCellRequest> requests)
    {
        if (requests.Count == 0)
        {
            return new StorageCellBulkImportResult(0, 0, 0, Array.Empty<string>());
        }

        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStorageCellsCommandTimeoutSeconds);

        var inserted = 0;
        var updated = 0;
        var failed = 0;
        var errors = new List<string>();

        // Per-row UPSERT по (warehouse_name, code). Без транзакции — частичный успех допустим.
        // На 200 записей даёт ~5-10s; ускорим UNIQUE constraint'ом если станет узким.
        const string findSql = """
            SELECT id FROM app_warehouse_storage_cells
            WHERE warehouse_name = @warehouse_name AND code = @code
            LIMIT 1;
            """;

        const string insertSql = """
            INSERT INTO app_warehouse_storage_cells (
                id, warehouse_name, code,
                zone_code, zone_name,
                row_no, rack_no, shelf_no, cell_no,
                cell_type, capacity, status_text,
                qr_payload, comment_text,
                created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @warehouse_name, @code,
                @zone_code, @zone_name,
                @row_no, @rack_no, @shelf_no, @cell_no,
                @cell_type, @capacity, @status_text,
                @qr_payload, @comment_text,
                UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            );
            """;

        const string updateSql = """
            UPDATE app_warehouse_storage_cells
            SET zone_code = @zone_code,
                zone_name = @zone_name,
                row_no = @row_no, rack_no = @rack_no,
                shelf_no = @shelf_no, cell_no = @cell_no,
                cell_type = @cell_type,
                capacity = @capacity,
                status_text = @status_text,
                comment_text = @comment_text,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;

        for (var rowIndex = 0; rowIndex < requests.Count; rowIndex++)
        {
            var request = requests[rowIndex];
            try
            {
                // Find existing
                Guid? existingId = null;
                using (var findCommand = connection.CreateCommand())
                {
                    findCommand.CommandText = findSql;
                    findCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
                    findCommand.Parameters.AddWithValue("@warehouse_name", request.WarehouseName);
                    findCommand.Parameters.AddWithValue("@code", request.Code);
                    using var reader = findCommand.ExecuteReader();
                    if (reader.Read())
                    {
                        existingId = Guid.Parse(reader.GetString(0));
                    }
                }

                if (existingId.HasValue)
                {
                    using var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = updateSql;
                    updateCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
                    BindRequestParameters(updateCommand, existingId.Value, request, includeQr: false);
                    updateCommand.ExecuteNonQuery();
                    updated++;
                }
                else
                {
                    using var insertCommand = connection.CreateCommand();
                    insertCommand.CommandText = insertSql;
                    insertCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
                    BindRequestParameters(insertCommand, Guid.NewGuid(), request, includeQr: true);
                    insertCommand.ExecuteNonQuery();
                    inserted++;
                }
            }
            catch (Exception exception)
            {
                failed++;
                errors.Add($"Строка {rowIndex + 1} (код {request.Code}): {exception.Message}");
            }
        }

        return new StorageCellBulkImportResult(inserted, updated, failed, errors);
    }

    public void DeleteStorageCell(Guid id)
    {
        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStorageCellsCommandTimeoutSeconds);

        const string sql = "DELETE FROM app_warehouse_storage_cells WHERE id = @id;";

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.ExecuteNonQuery();
    }

    private static void BindRequestParameters(MySqlCommand command, Guid id, StorageCellRequest request, bool includeQr)
    {
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@warehouse_name", request.WarehouseName);
        command.Parameters.AddWithValue("@code", request.Code);
        command.Parameters.AddWithValue("@zone_code", (object?)request.ZoneCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@zone_name", (object?)request.ZoneName ?? DBNull.Value);
        command.Parameters.AddWithValue("@row_no", request.RowNo);
        command.Parameters.AddWithValue("@rack_no", request.RackNo);
        command.Parameters.AddWithValue("@shelf_no", request.ShelfNo);
        command.Parameters.AddWithValue("@cell_no", request.CellNo);
        command.Parameters.AddWithValue("@cell_type", (object?)request.CellType ?? DBNull.Value);
        command.Parameters.AddWithValue("@capacity", request.Capacity);
        command.Parameters.AddWithValue("@status_text", (object?)request.StatusText ?? DBNull.Value);
        command.Parameters.AddWithValue("@comment_text", (object?)request.CommentText ?? DBNull.Value);

        if (includeQr)
        {
            // Sprint 3 Task 22 заменит generated payload на полноценный JSON через QRCoder.
            // Пока: простой text identifier.
            var qrPayload = $"bin:{id}:{request.Code}";
            command.Parameters.AddWithValue("@qr_payload", qrPayload);
        }
    }

    private static StorageCell ReadStorageCellRow(MySqlDataReader reader)
    {
        return new StorageCell(
            Id: Guid.Parse(reader.GetString(reader.GetOrdinal("id"))),
            Code: reader.GetString(reader.GetOrdinal("code")),
            WarehouseNodeId: null,
            WarehouseName: reader.GetString(reader.GetOrdinal("warehouse_name")),
            ZoneCode: reader.IsDBNull(reader.GetOrdinal("zone_code")) ? null : reader.GetString(reader.GetOrdinal("zone_code")),
            ZoneName: reader.IsDBNull(reader.GetOrdinal("zone_name")) ? null : reader.GetString(reader.GetOrdinal("zone_name")),
            RowNo: reader.GetInt32(reader.GetOrdinal("row_no")),
            RackNo: reader.GetInt32(reader.GetOrdinal("rack_no")),
            ShelfNo: reader.GetInt32(reader.GetOrdinal("shelf_no")),
            CellNo: reader.GetInt32(reader.GetOrdinal("cell_no")),
            CellType: reader.IsDBNull(reader.GetOrdinal("cell_type")) ? null : reader.GetString(reader.GetOrdinal("cell_type")),
            Capacity: reader.GetDecimal(reader.GetOrdinal("capacity")),
            StatusText: reader.IsDBNull(reader.GetOrdinal("status_text")) ? null : reader.GetString(reader.GetOrdinal("status_text")),
            QrPayload: reader.IsDBNull(reader.GetOrdinal("qr_payload")) ? null : reader.GetString(reader.GetOrdinal("qr_payload")),
            CommentText: reader.IsDBNull(reader.GetOrdinal("comment_text")) ? null : reader.GetString(reader.GetOrdinal("comment_text")),
            CreatedAtUtc: reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: reader.GetDateTime(reader.GetOrdinal("updated_at_utc")));
    }
}
