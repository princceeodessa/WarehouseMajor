using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

public sealed class MySqlWmsReadinessReader : IWmsReadinessReader
{
    private readonly MySqlExecutor _executor;

    public MySqlWmsReadinessReader(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<WmsReadinessSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            WITH
            b AS (
                SELECT
                    item_id,
                    warehouse_name,
                    SUM(quantity) AS qty,
                    SUM(reserved_quantity) AS reserved_qty
                FROM app_warehouse_stock_balances
                WHERE quantity <> 0 OR reserved_quantity <> 0
                GROUP BY item_id, warehouse_name
            ),
            l AS (
                SELECT
                    item_id,
                    warehouse_name,
                    SUM(quantity) AS qty,
                    SUM(reserved_quantity) AS reserved_qty
                FROM app_warehouse_stock_locations
                WHERE quantity <> 0 OR reserved_quantity <> 0
                GROUP BY item_id, warehouse_name
            ),
            all_keys AS (
                SELECT item_id, warehouse_name FROM b
                UNION
                SELECT item_id, warehouse_name FROM l
            ),
            mismatch AS (
                SELECT
                    COALESCE(l.qty, 0) - COALESCE(b.qty, 0) AS diff
                FROM all_keys k
                LEFT JOIN b ON b.item_id = k.item_id AND b.warehouse_name = k.warehouse_name
                LEFT JOIN l ON l.item_id = k.item_id AND l.warehouse_name = k.warehouse_name
                WHERE ABS(COALESCE(l.qty, 0) - COALESCE(b.qty, 0)) > 0.0001
            )
            SELECT
                (SELECT COUNT(*) FROM app_warehouse_storage_cells) AS cell_count,
                (SELECT COUNT(*) FROM app_warehouse_storage_cells WHERE code <> 'UNPLACED') AS real_cell_count,
                (SELECT COUNT(*) FROM app_warehouse_storage_cells WHERE code = 'UNPLACED') AS unplaced_cell_count,
                (SELECT COUNT(DISTINCT warehouse_name) FROM app_warehouse_storage_cells) AS warehouses_with_cells,
                (SELECT COUNT(*) FROM app_warehouse_stock_balances WHERE quantity <> 0 OR reserved_quantity <> 0) AS balance_rows,
                (SELECT COALESCE(SUM(quantity), 0) FROM app_warehouse_stock_balances WHERE quantity <> 0 OR reserved_quantity <> 0) AS balance_quantity,
                (SELECT COUNT(*) FROM app_warehouse_stock_locations WHERE quantity <> 0 OR reserved_quantity <> 0) AS location_rows,
                (SELECT COALESCE(SUM(quantity), 0) FROM app_warehouse_stock_locations WHERE quantity <> 0 OR reserved_quantity <> 0) AS location_quantity,
                (SELECT COUNT(*) FROM app_warehouse_stock_locations WHERE storage_cell_code <> 'UNPLACED' AND (quantity <> 0 OR reserved_quantity <> 0)) AS real_location_rows,
                (SELECT COALESCE(SUM(quantity), 0) FROM app_warehouse_stock_locations WHERE storage_cell_code <> 'UNPLACED' AND (quantity <> 0 OR reserved_quantity <> 0)) AS real_location_quantity,
                (SELECT COUNT(*) FROM app_warehouse_stock_locations WHERE storage_cell_code = 'UNPLACED' AND (quantity <> 0 OR reserved_quantity <> 0)) AS unplaced_rows,
                (SELECT COALESCE(SUM(quantity), 0) FROM app_warehouse_stock_locations WHERE storage_cell_code = 'UNPLACED' AND (quantity <> 0 OR reserved_quantity <> 0)) AS unplaced_quantity,
                (SELECT COUNT(*) FROM app_warehouse_stock_balances WHERE quantity < 0 OR reserved_quantity < 0) AS negative_balance_rows,
                (SELECT COALESCE(SUM(quantity), 0) FROM app_warehouse_stock_balances WHERE quantity < 0 OR reserved_quantity < 0) AS negative_balance_quantity,
                (SELECT COUNT(*) FROM mismatch) AS mismatched_pairs,
                (SELECT COALESCE(SUM(diff), 0) FROM mismatch) AS net_difference,
                (SELECT COALESCE(SUM(ABS(diff)), 0) FROM mismatch) AS absolute_difference,
                (SELECT MAX(projected_at_utc) FROM app_warehouse_stock_balances) AS latest_balance_projection_utc,
                (SELECT MAX(updated_at_utc) FROM app_warehouse_stock_locations) AS latest_location_update_utc;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new WmsReadinessSnapshot(
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null);
        }

        return new WmsReadinessSnapshot(
            ReadInt32(reader, "cell_count"),
            ReadInt32(reader, "real_cell_count"),
            ReadInt32(reader, "unplaced_cell_count"),
            ReadInt32(reader, "warehouses_with_cells"),
            ReadInt32(reader, "balance_rows"),
            ReadDecimal(reader, "balance_quantity"),
            ReadInt32(reader, "location_rows"),
            ReadDecimal(reader, "location_quantity"),
            ReadInt32(reader, "real_location_rows"),
            ReadDecimal(reader, "real_location_quantity"),
            ReadInt32(reader, "unplaced_rows"),
            ReadDecimal(reader, "unplaced_quantity"),
            ReadInt32(reader, "negative_balance_rows"),
            ReadDecimal(reader, "negative_balance_quantity"),
            ReadInt32(reader, "mismatched_pairs"),
            ReadDecimal(reader, "net_difference"),
            ReadDecimal(reader, "absolute_difference"),
            ReadDateTime(reader, "latest_balance_projection_utc"),
            ReadDateTime(reader, "latest_location_update_utc"));
    }

    private static int ReadInt32(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal));
    }

    private static decimal ReadDecimal(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(reader.GetValue(ordinal));
    }

    private static DateTime? ReadDateTime(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? null : reader.GetDateTime(ordinal);
    }
}
