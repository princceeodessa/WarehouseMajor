using MySqlConnector;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 1 (WMS остатки): чтение app_warehouse_stock_balances для UI.
// Источник наполняется проекцией Persistence/Sql/project-stock-balances.sql
// из stock_balances. В Sprint 2 источник заменится на pull через OData,
// этот reader остаётся тем же — UI зависит только от app_warehouse_stock_balances.
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlStockCommandTimeoutSeconds = 30;

    public IReadOnlyList<WarehouseStockRow> LoadStockBalances(
        string? warehouseNodeId = null,
        string? itemSearch = null,
        bool onlyPositive = false,
        int limit = 5000)
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStockCommandTimeoutSeconds);

            const string sql = """
                SELECT
                    id, item_id, item_code, item_name,
                    warehouse_node_id, warehouse_name,
                    quantity, reserved_quantity, available_quantity,
                    last_movement_at_utc, projected_at_utc
                FROM app_warehouse_stock_balances
                WHERE (@warehouse_node_id IS NULL OR warehouse_node_id = @warehouse_node_id)
                  AND (@item_search IS NULL
                       OR item_name LIKE @item_search
                       OR item_code LIKE @item_search)
                  AND (@only_positive = 0 OR quantity > 0)
                ORDER BY quantity DESC
                LIMIT @limit;
                """;

            using var command = CreateMySqlStockCommand(connection, sql);
            AddParameter(command, "@warehouse_node_id",
                string.IsNullOrWhiteSpace(warehouseNodeId) ? null : warehouseNodeId);
            AddParameter(command, "@item_search",
                string.IsNullOrWhiteSpace(itemSearch) ? null : $"%{itemSearch.Trim()}%");
            AddParameter(command, "@only_positive", onlyPositive ? 1 : 0);
            AddParameter(command, "@limit", Math.Max(1, limit));

            var rows = new List<WarehouseStockRow>(capacity: 1024);
            using var reader = command.ExecuteReader();

            var ordId = reader.GetOrdinal("id");
            var ordItemId = reader.GetOrdinal("item_id");
            var ordItemCode = reader.GetOrdinal("item_code");
            var ordItemName = reader.GetOrdinal("item_name");
            var ordWhId = reader.GetOrdinal("warehouse_node_id");
            var ordWhName = reader.GetOrdinal("warehouse_name");
            var ordQty = reader.GetOrdinal("quantity");
            var ordReserved = reader.GetOrdinal("reserved_quantity");
            var ordAvailable = reader.GetOrdinal("available_quantity");
            var ordLastMov = reader.GetOrdinal("last_movement_at_utc");
            var ordProjected = reader.GetOrdinal("projected_at_utc");

            while (reader.Read())
            {
                // id/item_id/warehouse_node_id — CHAR(36): MySqlConnector мапит их в Guid,
                // GetString кидает InvalidCastException → catch → пустые «Остатки».
                // ReadString (GetValue-based) — безопасен для Guid и string.
                rows.Add(new WarehouseStockRow(
                    Id: ReadString(reader, ordId),
                    ItemId: ReadString(reader, ordItemId),
                    ItemCode: reader.IsDBNull(ordItemCode) ? null : reader.GetString(ordItemCode),
                    ItemName: reader.IsDBNull(ordItemName) ? null : reader.GetString(ordItemName),
                    WarehouseNodeId: ReadString(reader, ordWhId),
                    WarehouseName: reader.IsDBNull(ordWhName) ? null : reader.GetString(ordWhName),
                    Quantity: reader.GetDecimal(ordQty),
                    ReservedQuantity: reader.GetDecimal(ordReserved),
                    AvailableQuantity: reader.IsDBNull(ordAvailable) ? 0m : reader.GetDecimal(ordAvailable),
                    LastMovementAtUtc: reader.GetDateTime(ordLastMov),
                    ProjectedAtUtc: reader.GetDateTime(ordProjected)));
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<WarehouseStockRow>();
        }
    }

    public IReadOnlyList<WarehouseStockSummary> LoadStockWarehouses()
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStockCommandTimeoutSeconds);

            const string sql = """
                SELECT
                    warehouse_node_id,
                    MAX(warehouse_name) AS warehouse_name,
                    COUNT(*) AS items_count,
                    SUM(quantity) AS total_quantity
                FROM app_warehouse_stock_balances
                GROUP BY warehouse_node_id
                ORDER BY total_quantity DESC;
                """;

            using var command = CreateMySqlStockCommand(connection, sql);

            var rows = new List<WarehouseStockSummary>();
            using var reader = command.ExecuteReader();

            var ordWhId = reader.GetOrdinal("warehouse_node_id");
            var ordWhName = reader.GetOrdinal("warehouse_name");
            var ordCount = reader.GetOrdinal("items_count");
            var ordTotal = reader.GetOrdinal("total_quantity");

            while (reader.Read())
            {
                // warehouse_node_id — CHAR(36) → Guid в MySqlConnector, GetString падает.
                rows.Add(new WarehouseStockSummary(
                    WarehouseNodeId: ReadString(reader, ordWhId),
                    WarehouseName: reader.IsDBNull(ordWhName) ? null : reader.GetString(ordWhName),
                    ItemsCount: reader.GetInt32(ordCount),
                    TotalQuantity: reader.IsDBNull(ordTotal) ? 0m : reader.GetDecimal(ordTotal)));
            }

            return rows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return Array.Empty<WarehouseStockSummary>();
        }
    }

    /// <summary>
    /// Запускает проекцию stock_balances → app_warehouse_stock_balances.
    /// Идемпотентна (UPSERT + tombstone). Возвращает количество спроецированных строк.
    /// </summary>
    public int RefreshStockBalancesProjection()
    {
        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlStockCommandTimeoutSeconds);

            using var transaction = connection.BeginTransaction();

            const string upsertSql = """
                INSERT INTO app_warehouse_stock_balances (
                    id, item_id, item_code, item_name,
                    warehouse_node_id, warehouse_name,
                    quantity, reserved_quantity, last_movement_at_utc,
                    projected_at_utc
                )
                SELECT
                    UUID() AS id,
                    sb.item_id,
                    ni.code AS item_code,
                    ni.name AS item_name,
                    sb.warehouse_node_id,
                    wn.name AS warehouse_name,
                    SUM(sb.quantity) AS quantity,
                    SUM(sb.reserved_quantity) AS reserved_quantity,
                    MAX(sb.last_movement_at_utc) AS last_movement_at_utc,
                    NOW(6) AS projected_at_utc
                FROM stock_balances sb
                LEFT JOIN nomenclature_items ni ON ni.id = sb.item_id
                LEFT JOIN warehouse_nodes wn ON wn.id = sb.warehouse_node_id
                GROUP BY sb.item_id, sb.warehouse_node_id, ni.code, ni.name, wn.name
                ON DUPLICATE KEY UPDATE
                    item_code = VALUES(item_code),
                    item_name = VALUES(item_name),
                    warehouse_name = VALUES(warehouse_name),
                    quantity = VALUES(quantity),
                    reserved_quantity = VALUES(reserved_quantity),
                    last_movement_at_utc = VALUES(last_movement_at_utc),
                    projected_at_utc = VALUES(projected_at_utc);
                """;

            int upsertedRows;
            using (var upsertCommand = CreateMySqlStockCommand(connection, upsertSql))
            {
                upsertCommand.Transaction = transaction;
                upsertedRows = upsertCommand.ExecuteNonQuery();
            }

            const string tombstoneSql = """
                DELETE app FROM app_warehouse_stock_balances app
                LEFT JOIN (
                    SELECT item_id, warehouse_node_id
                    FROM stock_balances
                    GROUP BY item_id, warehouse_node_id
                ) src ON src.item_id = app.item_id AND src.warehouse_node_id = app.warehouse_node_id
                WHERE src.item_id IS NULL;
                """;

            using (var tombstoneCommand = CreateMySqlStockCommand(connection, tombstoneSql))
            {
                tombstoneCommand.Transaction = transaction;
                tombstoneCommand.ExecuteNonQuery();
            }

            transaction.Commit();
            return upsertedRows;
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return 0;
        }
    }

    private static MySqlCommand CreateMySqlStockCommand(MySqlConnection connection, string commandText)
    {
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.CommandTimeout = MysqlStockCommandTimeoutSeconds;
        return command;
    }
}

public sealed record WarehouseStockRow(
    string Id,
    string ItemId,
    string? ItemCode,
    string? ItemName,
    string WarehouseNodeId,
    string? WarehouseName,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime LastMovementAtUtc,
    DateTime ProjectedAtUtc);

public sealed record WarehouseStockSummary(
    string WarehouseNodeId,
    string? WarehouseName,
    int ItemsCount,
    decimal TotalQuantity);
