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

    public IReadOnlyList<string> LoadWarehouseNames()
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
                SELECT name
                FROM (
                    SELECT name
                    FROM warehouse_nodes
                    WHERE name IS NOT NULL AND TRIM(name) <> ''

                    UNION

                    SELECT warehouse_name AS name
                    FROM app_warehouse_stock_balances
                    WHERE warehouse_name IS NOT NULL AND TRIM(warehouse_name) <> ''

                    UNION

                    SELECT warehouse_name AS name
                    FROM app_warehouse_storage_cells
                    WHERE warehouse_name IS NOT NULL AND TRIM(warehouse_name) <> ''
                ) AS warehouses
                ORDER BY name;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;

            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var name = NormalizeWarehouseName(reader.GetString(0));
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names.Add(name);
                }
            }

            return names.ToArray();
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<string>();
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

        EnsureStorageCellCodeIsAvailable(connection, request.WarehouseName, request.Code);

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

        try
        {
            command.ExecuteNonQuery();
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            // Гонка между пред-проверкой и INSERT: UNIQUE-индекс сработал — отдаём
            // то же дружелюбное сообщение вместо сырого "Duplicate entry ... for key".
            throw new InvalidOperationException(BuildDuplicateCellMessage(request.WarehouseName, request.Code), ex);
        }

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

        EnsureStorageCellCodeIsAvailable(connection, request.WarehouseName, request.Code, id);

        // QR payload пересобираем при Update — warehouse_name или code могли измениться,
        // и handheld должен видеть актуальные данные.
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
                qr_payload = @qr_payload,
                comment_text = @comment_text,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;

        // storage_cell_code и warehouse_name денормализованы в app_warehouse_stock_locations —
        // при переименовании ячейки или смене склада копии обновляем атомарно с ячейкой,
        // иначе остатки и ТСД продолжат показывать старый код.
        const string syncLocationsSql = """
            UPDATE app_warehouse_stock_locations
            SET storage_cell_code = @code,
                warehouse_name = @warehouse_name,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE storage_cell_id = @id;
            """;

        using var transaction = connection.BeginTransaction();

        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            BindRequestParameters(command, id, request, includeQr: true);

            try
            {
                command.ExecuteNonQuery();
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                throw new InvalidOperationException(BuildDuplicateCellMessage(request.WarehouseName, request.Code), ex);
            }
        }

        using (var syncCommand = connection.CreateCommand())
        {
            syncCommand.Transaction = transaction;
            syncCommand.CommandText = syncLocationsSql;
            syncCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            syncCommand.Parameters.AddWithValue("@id", id.ToString());
            syncCommand.Parameters.AddWithValue("@code", NormalizeCellCode(request.Code));
            syncCommand.Parameters.AddWithValue("@warehouse_name", NormalizeWarehouseName(request.WarehouseName));
            syncCommand.ExecuteNonQuery();
        }

        transaction.Commit();
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
                qr_payload = @qr_payload,
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
                    findCommand.Parameters.AddWithValue("@warehouse_name", NormalizeWarehouseName(request.WarehouseName));
                    findCommand.Parameters.AddWithValue("@code", NormalizeCellCode(request.Code));
                    using var reader = findCommand.ExecuteReader();
                    if (reader.Read())
                    {
                        existingId = ReadGuid(reader, 0);
                    }
                }

                if (existingId.HasValue)
                {
                    using var updateCommand = connection.CreateCommand();
                    updateCommand.CommandText = updateSql;
                    updateCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
                    BindRequestParameters(updateCommand, existingId.Value, request, includeQr: true);
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

        using var transaction = connection.BeginTransaction();

        // Ячейку с товаром удалять нельзя — строки размещения осиротеют молча,
        // и остаток «повиснет» в несуществующей ячейке.
        const string stockCheckSql = """
            SELECT COALESCE(SUM(quantity), 0), COALESCE(SUM(reserved_quantity), 0)
            FROM app_warehouse_stock_locations
            WHERE storage_cell_id = @id;
            """;

        using (var checkCommand = connection.CreateCommand())
        {
            checkCommand.Transaction = transaction;
            checkCommand.CommandText = stockCheckSql;
            checkCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            checkCommand.Parameters.AddWithValue("@id", id.ToString());

            using var reader = checkCommand.ExecuteReader();
            if (reader.Read())
            {
                var quantity = reader.GetDecimal(0);
                var reserved = reader.GetDecimal(1);
                if (quantity > 0 || reserved > 0)
                {
                    throw new InvalidOperationException(
                        $"В ячейке остаток {quantity:0.####} (в резерве {reserved:0.####}). " +
                        "Перед удалением переместите товар в другую ячейку.");
                }
            }
        }

        // Нулевые строки размещения подчищаем вместе с ячейкой.
        using (var cleanupCommand = connection.CreateCommand())
        {
            cleanupCommand.Transaction = transaction;
            cleanupCommand.CommandText = "DELETE FROM app_warehouse_stock_locations WHERE storage_cell_id = @id;";
            cleanupCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            cleanupCommand.Parameters.AddWithValue("@id", id.ToString());
            cleanupCommand.ExecuteNonQuery();
        }

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM app_warehouse_storage_cells WHERE id = @id;";
            deleteCommand.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
            deleteCommand.Parameters.AddWithValue("@id", id.ToString());
            deleteCommand.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    private static void BindRequestParameters(MySqlCommand command, Guid id, StorageCellRequest request, bool includeQr)
    {
        var warehouseName = NormalizeWarehouseName(request.WarehouseName);
        // Trim обязателен: коллация utf8mb4_0900_ai_ci — NO PAD, для MySQL "A-01 " и "A-01"
        // разные значения. Без нормализации пробел обходит и пред-проверку дублей,
        // и UNIQUE-индекс, и попадает в QR-payload.
        var code = NormalizeCellCode(request.Code);
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@warehouse_name", warehouseName);
        command.Parameters.AddWithValue("@code", code);
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
            // MWH (MajorWarehouse) формат QR-payload — совместим с TsdScanValueParser в WarehouseAutomatisaion.Tsd.
            // Формат: MWH|v=1|type=cell|warehouse=<url-encoded>|cell=<url-encoded code>
            // Pipe-separated key=value, UTF-8 значения URL-encoded для безопасной передачи через QR.
            command.Parameters.AddWithValue("@qr_payload", BuildMwhCellPayload(warehouseName, code));
        }
    }

    private static void EnsureStorageCellCodeIsAvailable(
        MySqlConnection connection,
        string warehouseName,
        string code,
        Guid? excludedId = null)
    {
        const string sql = """
            SELECT id
            FROM app_warehouse_storage_cells
            WHERE warehouse_name = @warehouse_name
              AND code = @code
              AND (@excluded_id IS NULL OR id <> @excluded_id)
            LIMIT 1;
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlStorageCellsCommandTimeoutSeconds;
        command.Parameters.AddWithValue("@warehouse_name", NormalizeWarehouseName(warehouseName));
        command.Parameters.AddWithValue("@code", NormalizeCellCode(code));
        command.Parameters.AddWithValue(
            "@excluded_id",
            excludedId.HasValue ? excludedId.Value.ToString() : DBNull.Value);

        if (command.ExecuteScalar() is not null)
        {
            throw new InvalidOperationException(BuildDuplicateCellMessage(warehouseName, code));
        }
    }

    private static string BuildDuplicateCellMessage(string warehouseName, string code)
    {
        return $"Ячейка «{NormalizeCellCode(code)}» уже существует на складе «{NormalizeWarehouseName(warehouseName)}».";
    }

    /// <summary>
    /// Генерирует QR-payload в формате MWH (MajorWarehouse) для ячейки склада.
    /// Парсится TsdScanValueParser.TryParseSystemQr — handheld'ы сразу определяют type=cell
    /// и извлекают warehouse + cell для матча в БД.
    /// </summary>
    public static string BuildMwhCellPayload(string warehouseName, string cellCode)
    {
        warehouseName = NormalizeWarehouseName(warehouseName);
        var parts = new[]
        {
            "MWH",
            "v=1",
            "type=cell",
            $"warehouse={Uri.EscapeDataString(warehouseName ?? string.Empty)}",
            $"cell={Uri.EscapeDataString(cellCode ?? string.Empty)}"
        };
        return string.Join("|", parts);
    }

    private static string NormalizeWarehouseName(string? warehouseName)
    {
        return (warehouseName ?? string.Empty)
            .Trim()
            .Replace('Ё', 'Е')
            .Replace('ё', 'е');
    }

    private static string NormalizeCellCode(string? code)
    {
        return (code ?? string.Empty).Trim();
    }

    private static StorageCell ReadStorageCellRow(MySqlDataReader reader)
    {
        return new StorageCell(
            Id: ReadGuid(reader, reader.GetOrdinal("id")),
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
