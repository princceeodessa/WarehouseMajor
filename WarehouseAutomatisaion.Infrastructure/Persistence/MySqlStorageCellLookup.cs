using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// Поиск ячейки по сканировке. Стратегии (все в одном SQL):
//   - точное совпадение code / id / qr_payload
//   - совпадение по нормализованному значению (UPPER + удаление пробелов/-/_/./)
// Порядок ORDER BY — точные совпадения раньше нормализованных.
public sealed class MySqlStorageCellLookup : IStorageCellLookup
{
    private readonly MySqlExecutor _executor;

    public MySqlStorageCellLookup(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<StorageCellLookupMatch?> FindAsync(string scanValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scanValue))
        {
            return null;
        }

        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = $$"""
            SELECT id, warehouse_name, code
            FROM app_warehouse_storage_cells
            WHERE code = @raw_value
                OR id = @raw_value
                OR qr_payload = @raw_value
                OR {{ScanValueNormalizer.NormalizeSql("code")}} = @normalized_value
                OR {{ScanValueNormalizer.NormalizeSql("qr_payload")}} = @normalized_value
            ORDER BY
                CASE
                    WHEN code = @raw_value THEN 0
                    WHEN {{ScanValueNormalizer.NormalizeSql("code")}} = @normalized_value THEN 1
                    ELSE 2
                END,
                warehouse_name,
                code
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@raw_value", scanValue.Trim());
        command.Parameters.AddWithValue("@normalized_value", ScanValueNormalizer.Normalize(scanValue));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new StorageCellLookupMatch(
            ScanValueNormalizer.ReadGuid(reader, "id"),
            ScanValueNormalizer.ReadString(reader, "code"),
            ScanValueNormalizer.ReadString(reader, "warehouse_name"));
    }
}
