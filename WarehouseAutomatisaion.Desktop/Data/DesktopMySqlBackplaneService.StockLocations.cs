using MySqlConnector;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Data;

// Фаза A: persistence для app_warehouse_stock_locations.
// CRUD-уровень. Бизнес-логика (вычитание при перемещении и т.д.) живёт выше.
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlStockLocationsCommandTimeoutSeconds = 30;

    public IReadOnlyList<StockLocation> LoadStockLocationsByCell(Guid storageCellId)
    {
        return QueryStockLocations(
            "storage_cell_id = @filter",
            "@filter", storageCellId.ToString());
    }

    public IReadOnlyList<StockLocation> LoadStockLocationsByItem(Guid itemId)
    {
        return QueryStockLocations(
            "item_id = @filter",
            "@filter", itemId.ToString());
    }

    public IReadOnlyList<StockLocation> LoadStockLocationsByWarehouse(string warehouseName)
    {
        return QueryStockLocations(
            "warehouse_name = @filter",
            "@filter", warehouseName);
    }

    public void UpsertStockLocation(StockLocationUpsert upsert)
    {
        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStockLocationsCommandTimeoutSeconds);

        const string sql = """
            INSERT INTO app_warehouse_stock_locations (
                id, item_id, item_code, item_name,
                warehouse_node_id, warehouse_name,
                storage_cell_id, storage_cell_code,
                quantity, reserved_quantity, last_movement_at_utc,
                created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @item_id, @item_code, @item_name,
                @warehouse_node_id, @warehouse_name,
                @storage_cell_id, @storage_cell_code,
                @quantity, @reserved_quantity, UTC_TIMESTAMP(6),
                UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            )
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
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlStockLocationsCommandTimeoutSeconds;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@item_id", upsert.ItemId.ToString());
        command.Parameters.AddWithValue("@item_code", (object?)upsert.ItemCode ?? DBNull.Value);
        command.Parameters.AddWithValue("@item_name", (object?)upsert.ItemName ?? DBNull.Value);
        command.Parameters.AddWithValue("@warehouse_node_id", upsert.WarehouseNodeId.HasValue
            ? upsert.WarehouseNodeId.Value.ToString()
            : DBNull.Value);
        command.Parameters.AddWithValue("@warehouse_name", upsert.WarehouseName);
        command.Parameters.AddWithValue("@storage_cell_id", upsert.StorageCellId.ToString());
        command.Parameters.AddWithValue("@storage_cell_code", upsert.StorageCellCode);
        command.Parameters.AddWithValue("@quantity", upsert.Quantity);
        command.Parameters.AddWithValue("@reserved_quantity", upsert.ReservedQuantity);
        command.ExecuteNonQuery();
    }

    public void DeleteStockLocation(Guid id)
    {
        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlStockLocationsCommandTimeoutSeconds);

        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM app_warehouse_stock_locations WHERE id = @id;";
        command.CommandTimeout = MysqlStockLocationsCommandTimeoutSeconds;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.ExecuteNonQuery();
    }

    private IReadOnlyList<StockLocation> QueryStockLocations(string whereClause, string paramName, string paramValue)
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStockLocationsCommandTimeoutSeconds);

            var sql = $"""
                SELECT
                    id, item_id, item_code, item_name,
                    warehouse_node_id, warehouse_name,
                    storage_cell_id, storage_cell_code,
                    quantity, reserved_quantity, available_quantity,
                    last_movement_at_utc, created_at_utc, updated_at_utc
                FROM app_warehouse_stock_locations
                WHERE {whereClause}
                ORDER BY last_movement_at_utc DESC;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlStockLocationsCommandTimeoutSeconds;
            command.Parameters.AddWithValue(paramName, paramValue);

            var rows = new List<StockLocation>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(ReadStockLocationRow(reader));
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<StockLocation>();
        }
    }

    private static StockLocation ReadStockLocationRow(MySqlDataReader reader)
    {
        // MySqlConnector auto-конвертирует CHAR(36) → Guid когда UUID. GetValue+ToString универсально.
        var warehouseNodeIdRaw = reader.IsDBNull(reader.GetOrdinal("warehouse_node_id"))
            ? null
            : reader.GetValue(reader.GetOrdinal("warehouse_node_id"))?.ToString();
        Guid? warehouseNodeId = Guid.TryParse(warehouseNodeIdRaw, out var whId) ? whId : null;

        return new StockLocation(
            Id: Guid.Parse(reader.GetValue(reader.GetOrdinal("id"))!.ToString()!),
            ItemId: Guid.Parse(reader.GetValue(reader.GetOrdinal("item_id"))!.ToString()!),
            ItemCode: reader.IsDBNull(reader.GetOrdinal("item_code"))
                ? string.Empty
                : reader.GetValue(reader.GetOrdinal("item_code"))?.ToString() ?? string.Empty,
            ItemName: reader.IsDBNull(reader.GetOrdinal("item_name"))
                ? string.Empty
                : reader.GetValue(reader.GetOrdinal("item_name"))?.ToString() ?? string.Empty,
            WarehouseNodeId: warehouseNodeId,
            WarehouseName: reader.GetValue(reader.GetOrdinal("warehouse_name"))?.ToString() ?? string.Empty,
            StorageCellId: Guid.Parse(reader.GetValue(reader.GetOrdinal("storage_cell_id"))!.ToString()!),
            StorageCellCode: reader.GetValue(reader.GetOrdinal("storage_cell_code"))?.ToString() ?? string.Empty,
            Quantity: reader.GetDecimal(reader.GetOrdinal("quantity")),
            ReservedQuantity: reader.GetDecimal(reader.GetOrdinal("reserved_quantity")),
            AvailableQuantity: reader.IsDBNull(reader.GetOrdinal("available_quantity"))
                ? 0m
                : reader.GetDecimal(reader.GetOrdinal("available_quantity")),
            LastMovementAtUtc: reader.GetDateTime(reader.GetOrdinal("last_movement_at_utc")),
            CreatedAtUtc: reader.GetDateTime(reader.GetOrdinal("created_at_utc")),
            UpdatedAtUtc: reader.GetDateTime(reader.GetOrdinal("updated_at_utc")));
    }
}
