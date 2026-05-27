using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// Две стратегии поиска товара:
//   1) по баркоду через app_product_barcodes (если таблица существует) → JOIN catalog
//   2) по app_catalog_items: code / id / barcode_value / qr_payload, плюс fuzzy
//      по name (длина >= 4 символа нормализованных).
// При отсутствии таблицы app_product_barcodes (MySqlException 1146) — переход на (2).
public sealed class MySqlProductBarcodeLookup : IProductBarcodeLookup
{
    private readonly MySqlExecutor _executor;

    public MySqlProductBarcodeLookup(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<ProductBarcodeMatch?> FindAsync(string scanValue, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scanValue))
        {
            return null;
        }

        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);

        var byBarcode = await TryFindByBarcodeAsync(connection, scanValue, cancellationToken);
        if (byBarcode is not null)
        {
            return byBarcode;
        }

        return await TryFindByCatalogAsync(connection, scanValue, cancellationToken);
    }

    private static async Task<ProductBarcodeMatch?> TryFindByBarcodeAsync(
        MySqlConnection connection,
        string scanValue,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
            command.CommandText = $$"""
                SELECT
                    COALESCE(nomenclature.id, item.id) AS id,
                    item.code,
                    item.name
                FROM app_product_barcodes barcode
                INNER JOIN app_catalog_items item ON item.code = barcode.item_code
                LEFT JOIN nomenclature_items nomenclature ON nomenclature.code = item.code
                WHERE barcode.barcode_value = @raw_value
                    OR {{ScanValueNormalizer.NormalizeSql("barcode.barcode_value")}} = @normalized_value
                ORDER BY
                    CASE
                        WHEN barcode.barcode_value = @raw_value THEN 0
                        ELSE 1
                    END,
                    item.name,
                    item.code
                LIMIT 1;
                """;
            BindScanParameters(command, scanValue);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return ReadMatch(reader);
        }
        catch (MySqlException exception) when (exception.Number is 1146)
        {
            return null;
        }
    }

    private static async Task<ProductBarcodeMatch?> TryFindByCatalogAsync(
        MySqlConnection connection,
        string scanValue,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = $$"""
            SELECT
                COALESCE(nomenclature.id, item.id) AS id,
                item.code,
                item.name
            FROM app_catalog_items item
            LEFT JOIN nomenclature_items nomenclature ON nomenclature.code = item.code
            WHERE item.code = @raw_value
                OR item.id = @raw_value
                OR item.barcode_value = @raw_value
                OR item.qr_payload = @raw_value
                OR {{ScanValueNormalizer.NormalizeSql("item.code")}} = @normalized_value
                OR {{ScanValueNormalizer.NormalizeSql("item.barcode_value")}} = @normalized_value
                OR {{ScanValueNormalizer.NormalizeSql("item.qr_payload")}} = @normalized_value
                OR (@allow_name_search = 1 AND {{ScanValueNormalizer.NormalizeSql("item.name")}} LIKE @normalized_like)
            ORDER BY
                CASE
                    WHEN item.code = @raw_value THEN 0
                    WHEN item.barcode_value = @raw_value THEN 1
                    WHEN item.qr_payload = @raw_value THEN 2
                    WHEN {{ScanValueNormalizer.NormalizeSql("item.code")}} = @normalized_value THEN 3
                    ELSE 4
                END,
                item.name,
                item.code
            LIMIT 1;
            """;
        BindScanParameters(command, scanValue);

        var normalizedValue = ScanValueNormalizer.Normalize(scanValue);
        command.Parameters.AddWithValue("@allow_name_search", normalizedValue.Length >= 4 ? 1 : 0);
        command.Parameters.AddWithValue("@normalized_like", $"%{normalizedValue}%");

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return ReadMatch(reader);
    }

    private static void BindScanParameters(MySqlCommand command, string scanValue)
    {
        command.Parameters.AddWithValue("@raw_value", scanValue.Trim());
        command.Parameters.AddWithValue("@normalized_value", ScanValueNormalizer.Normalize(scanValue));
    }

    private static ProductBarcodeMatch ReadMatch(MySqlDataReader reader)
    {
        return new ProductBarcodeMatch(
            ScanValueNormalizer.ReadGuid(reader, "id"),
            ScanValueNormalizer.ReadString(reader, "code"),
            ScanValueNormalizer.ReadString(reader, "name"));
    }
}
