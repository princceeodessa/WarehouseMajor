using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5 learning loop: persistence для app_invoice_match_overrides.
// UPSERT по UNIQUE (normalized_text) — повторное сохранение увеличивает usage_count.
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlOverridesCommandTimeoutSeconds = 15;

    public NomenclatureRef? FindInvoiceMatchOverride(string normalizedText)
    {
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            return null;
        }

        try
        {
            EnsureDatabaseAndSchema();

            using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
                _options,
                useDatabase: true,
                MysqlConnectTimeoutSeconds,
                MysqlOverridesCommandTimeoutSeconds);

            const string sql = """
                SELECT matched_item_id, matched_item_code, matched_item_name
                FROM app_invoice_match_overrides
                WHERE normalized_text = @normalized_text
                LIMIT 1;
                """;

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = MysqlOverridesCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@normalized_text", normalizedText);

            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                return null;
            }

            // Side-effect: увеличиваем usage_count + last_used_at_utc (fire-and-forget, separate call).
            var itemId = reader.GetValue(0)?.ToString() ?? string.Empty;
            var itemCode = reader.IsDBNull(1) ? string.Empty : reader.GetValue(1)?.ToString() ?? string.Empty;
            var itemName = reader.IsDBNull(2) ? string.Empty : reader.GetValue(2)?.ToString() ?? string.Empty;

            reader.Close();
            TouchOverride(connection, normalizedText);

            return new NomenclatureRef(itemId, itemCode, itemName);
        }
        catch (Exception exception)
        {
            TryWriteErrorLog(exception);
            return null;
        }
    }

    public void SaveInvoiceMatchOverride(
        string recognizedText,
        string normalizedText,
        NomenclatureRef matchedItem,
        string actor,
        string? supplierName)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || string.IsNullOrWhiteSpace(matchedItem.Id))
        {
            return;
        }

        EnsureDatabaseAndSchema();

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlOverridesCommandTimeoutSeconds);

        const string sql = """
            INSERT INTO app_invoice_match_overrides (
                id, recognized_text, normalized_text,
                matched_item_id, matched_item_code, matched_item_name,
                supplier_name, created_by_actor,
                usage_count, last_used_at_utc, created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @recognized_text, @normalized_text,
                @matched_item_id, @matched_item_code, @matched_item_name,
                @supplier_name, @actor,
                1, UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                recognized_text = VALUES(recognized_text),
                matched_item_id = VALUES(matched_item_id),
                matched_item_code = VALUES(matched_item_code),
                matched_item_name = VALUES(matched_item_name),
                supplier_name = COALESCE(VALUES(supplier_name), supplier_name),
                usage_count = usage_count + 1,
                last_used_at_utc = UTC_TIMESTAMP(6),
                updated_at_utc = UTC_TIMESTAMP(6);
            """;

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.CommandTimeout = MysqlOverridesCommandTimeoutSeconds;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@recognized_text", Truncate(recognizedText, 512));
        command.Parameters.AddWithValue("@normalized_text", Truncate(normalizedText, 512));
        command.Parameters.AddWithValue("@matched_item_id", matchedItem.Id);
        command.Parameters.AddWithValue("@matched_item_code", (object?)matchedItem.Code ?? DBNull.Value);
        command.Parameters.AddWithValue("@matched_item_name", (object?)matchedItem.Name ?? DBNull.Value);
        command.Parameters.AddWithValue("@supplier_name", (object?)supplierName ?? DBNull.Value);
        command.Parameters.AddWithValue("@actor", string.IsNullOrWhiteSpace(actor) ? "system" : actor);
        command.ExecuteNonQuery();
    }

    private static void TouchOverride(MySqlConnection connection, string normalizedText)
    {
        try
        {
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE app_invoice_match_overrides
                SET usage_count = usage_count + 1, last_used_at_utc = UTC_TIMESTAMP(6)
                WHERE normalized_text = @normalized_text;
                """;
            command.CommandTimeout = MysqlOverridesCommandTimeoutSeconds;
            command.Parameters.AddWithValue("@normalized_text", normalizedText);
            command.ExecuteNonQuery();
        }
        catch
        {
            // Best-effort — статистика не критична. Основной find уже отработал.
        }
    }

    private static string Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= max ? value : value[..max];
    }
}
