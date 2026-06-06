using System.Globalization;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

public sealed class MySqlStockLocationBootstrapper : IStockLocationBootstrapper
{
    private const string UnplacedCellCode = "UNPLACED";
    private readonly MySqlExecutor _executor;

    public MySqlStockLocationBootstrapper(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<StockLocationBootstrapResult> BootstrapUnplacedAsync(
        string actorUserName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var source = await ReadSourceSummaryAsync(connection, transaction, cancellationToken);
        var cellsCreated = await EnsureUnplacedCellsAsync(connection, transaction, cancellationToken);
        var locationsAffected = await UpsertUnplacedLocationsAsync(connection, transaction, cancellationToken);
        locationsAffected += await ReconcileExistingUnplacedLocationsAsync(
            connection,
            transaction,
            cancellationToken);
        var operationId = Guid.NewGuid();

        await WriteOperationLogAsync(
            connection,
            transaction,
            operationId,
            actorUserName,
            source.Rows,
            source.Quantity,
            cellsCreated,
            locationsAffected,
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new StockLocationBootstrapResult(
            source.Rows,
            source.Quantity,
            cellsCreated,
            locationsAffected,
            operationId);
    }

    private static async Task<(int Rows, decimal Quantity)> ReadSourceSummaryAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            SELECT COUNT(*) AS rows_count,
                   COALESCE(SUM(quantity), 0) AS total_quantity
            FROM app_warehouse_stock_balances
            WHERE quantity <> 0 OR reserved_quantity <> 0;
            """);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return (0, 0m);
        }

        return (
            reader.IsDBNull(0) ? 0 : reader.GetInt32(0),
            reader.IsDBNull(1) ? 0m : reader.GetDecimal(1));
    }

    private static async Task<int> EnsureUnplacedCellsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
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
                comment_text,
                created_at_utc,
                updated_at_utc
            )
            SELECT
                UUID(),
                src.warehouse_name,
                @cell_code,
                'SYS',
                'Неразмещено',
                0,
                0,
                0,
                0,
                'system-unplaced',
                0,
                'Активна',
                CONCAT('MWH|v=1|type=cell|warehouse=', src.warehouse_name, '|cell=', @cell_code),
                'Системная ячейка для первичного размещения остатков из 1С/stock_balances.',
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            FROM (
                SELECT DISTINCT COALESCE(NULLIF(warehouse_name, ''), CONCAT('Склад ', warehouse_node_id)) AS warehouse_name
                FROM app_warehouse_stock_balances
                WHERE quantity <> 0 OR reserved_quantity <> 0
            ) src
            LEFT JOIN app_warehouse_storage_cells existing
                ON REPLACE(LOWER(TRIM(existing.warehouse_name)), 'ё', 'е')
                 = REPLACE(LOWER(TRIM(src.warehouse_name)), 'ё', 'е')
               AND existing.code = @cell_code
            WHERE existing.id IS NULL;
            """);

        command.Parameters.AddWithValue("@cell_code", UnplacedCellCode);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> UpsertUnplacedLocationsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            INSERT INTO app_warehouse_stock_locations (
                id,
                item_id,
                item_code,
                item_name,
                warehouse_node_id,
                warehouse_name,
                storage_cell_id,
                storage_cell_code,
                quantity,
                reserved_quantity,
                last_movement_at_utc,
                created_at_utc,
                updated_at_utc
            )
            SELECT
                UUID(),
                calc.item_id,
                calc.item_code,
                calc.item_name,
                calc.warehouse_node_id,
                calc.warehouse_name,
                calc.storage_cell_id,
                @cell_code,
                calc.unplaced_quantity,
                LEAST(calc.unplaced_quantity, calc.unplaced_reserved_quantity),
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6)
            FROM (
                SELECT
                    b.item_id,
                    b.item_code,
                    b.item_name,
                    b.warehouse_node_id,
                    COALESCE(NULLIF(b.warehouse_name, ''), CONCAT('Склад ', b.warehouse_node_id)) AS warehouse_name,
                    c.storage_cell_id,
                    GREATEST(b.quantity - COALESCE(placed.placed_quantity, 0), 0) AS unplaced_quantity,
                    GREATEST(b.reserved_quantity - COALESCE(placed.placed_reserved_quantity, 0), 0) AS unplaced_reserved_quantity
                FROM app_warehouse_stock_balances b
                JOIN (
                    SELECT
                        REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е') AS warehouse_key,
                        MIN(id) AS storage_cell_id
                    FROM app_warehouse_storage_cells
                    WHERE code = @cell_code
                    GROUP BY REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е')
                ) c ON c.warehouse_key = REPLACE(
                    LOWER(TRIM(COALESCE(NULLIF(b.warehouse_name, ''), CONCAT('Склад ', b.warehouse_node_id)))),
                    'ё',
                    'е')
                LEFT JOIN (
                    SELECT
                        item_id,
                        REPLACE(
                            LOWER(TRIM(COALESCE(NULLIF(warehouse_name, ''), CONCAT('Склад ', warehouse_node_id)))),
                            'ё',
                            'е') AS warehouse_key,
                        SUM(quantity) AS placed_quantity,
                        SUM(reserved_quantity) AS placed_reserved_quantity
                    FROM app_warehouse_stock_locations
                    WHERE storage_cell_code <> @cell_code
                    GROUP BY item_id, REPLACE(
                        LOWER(TRIM(COALESCE(NULLIF(warehouse_name, ''), CONCAT('Склад ', warehouse_node_id)))),
                        'ё',
                        'е')
                ) placed
                    ON placed.item_id = b.item_id
                   AND placed.warehouse_key = REPLACE(
                       LOWER(TRIM(COALESCE(NULLIF(b.warehouse_name, ''), CONCAT('Склад ', b.warehouse_node_id)))),
                       'ё',
                       'е')
                WHERE b.quantity <> 0 OR b.reserved_quantity <> 0
            ) calc
            ON DUPLICATE KEY UPDATE
                item_code = VALUES(item_code),
                item_name = VALUES(item_name),
                warehouse_node_id = VALUES(warehouse_node_id),
                warehouse_name = VALUES(warehouse_name),
                storage_cell_code = VALUES(storage_cell_code),
                quantity = VALUES(quantity),
                reserved_quantity = VALUES(reserved_quantity),
                last_movement_at_utc = UTC_TIMESTAMP(6),
                updated_at_utc = UTC_TIMESTAMP(6);
            """);

        command.Parameters.AddWithValue("@cell_code", UnplacedCellCode);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<int> ReconcileExistingUnplacedLocationsAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
            UPDATE app_warehouse_stock_locations location
            LEFT JOIN (
                SELECT
                    item_id,
                    REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е') AS warehouse_key,
                    SUM(quantity) AS balance_quantity,
                    SUM(reserved_quantity) AS balance_reserved_quantity
                FROM app_warehouse_stock_balances
                GROUP BY item_id, REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е')
            ) balance
                ON balance.item_id = location.item_id
               AND balance.warehouse_key = REPLACE(LOWER(TRIM(location.warehouse_name)), 'ё', 'е')
            LEFT JOIN (
                SELECT
                    item_id,
                    REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е') AS warehouse_key,
                    SUM(quantity) AS placed_quantity,
                    SUM(reserved_quantity) AS placed_reserved_quantity
                FROM app_warehouse_stock_locations
                WHERE storage_cell_code <> @cell_code
                GROUP BY item_id, REPLACE(LOWER(TRIM(warehouse_name)), 'ё', 'е')
            ) placed
                ON placed.item_id = location.item_id
               AND placed.warehouse_key = REPLACE(LOWER(TRIM(location.warehouse_name)), 'ё', 'е')
            SET
                location.quantity = GREATEST(
                    COALESCE(balance.balance_quantity, 0) - COALESCE(placed.placed_quantity, 0),
                    0),
                location.reserved_quantity = LEAST(
                    GREATEST(
                        COALESCE(balance.balance_quantity, 0) - COALESCE(placed.placed_quantity, 0),
                        0),
                    GREATEST(
                        COALESCE(balance.balance_reserved_quantity, 0) - COALESCE(placed.placed_reserved_quantity, 0),
                        0)),
                location.last_movement_at_utc = UTC_TIMESTAMP(6),
                location.updated_at_utc = UTC_TIMESTAMP(6)
            WHERE location.storage_cell_code = @cell_code
              AND (
                  location.quantity <> GREATEST(
                      COALESCE(balance.balance_quantity, 0) - COALESCE(placed.placed_quantity, 0),
                      0)
                  OR location.reserved_quantity <> LEAST(
                      GREATEST(
                          COALESCE(balance.balance_quantity, 0) - COALESCE(placed.placed_quantity, 0),
                          0),
                      GREATEST(
                          COALESCE(balance.balance_reserved_quantity, 0) - COALESCE(placed.placed_reserved_quantity, 0),
                          0))
              );
            """);

        command.Parameters.AddWithValue("@cell_code", UnplacedCellCode);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteOperationLogAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid operationId,
        string actorUserName,
        int sourceRows,
        decimal sourceQuantity,
        int cellsCreated,
        int locationsAffected,
        CancellationToken cancellationToken)
    {
        await using var command = CreateCommand(connection, transaction, """
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
                UTC_TIMESTAMP(6),
                @actor_user_name,
                'stock-location-bootstrap',
                @id,
                @entity_number,
                'Инициализация адресных остатков',
                'OK',
                @message_text
            );
            """);

        command.Parameters.AddWithValue("@id", operationId.ToString());
        command.Parameters.AddWithValue("@actor_user_name",
            string.IsNullOrWhiteSpace(actorUserName) ? Environment.UserName : actorUserName.Trim());
        command.Parameters.AddWithValue("@entity_number", UnplacedCellCode);
        command.Parameters.AddWithValue("@message_text",
            string.Format(
                CultureInfo.InvariantCulture,
                "Source rows: {0}; source quantity: {1:0.####}; cells created: {2}; stock-location rows affected: {3}.",
                sourceRows,
                sourceQuantity,
                cellsCreated,
                locationsAffected));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static MySqlCommand CreateCommand(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string commandText)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = commandText;
        return command;
    }
}
