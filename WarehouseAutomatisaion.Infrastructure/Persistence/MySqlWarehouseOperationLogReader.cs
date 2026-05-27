using System.Globalization;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

public sealed class MySqlWarehouseOperationLogReader : IWarehouseOperationLogReader
{
    private readonly MySqlExecutor _executor;

    public MySqlWarehouseOperationLogReader(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<WarehouseOperationLogRecord>> GetRecentAsync(
        int limit,
        string? search,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
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
            WHERE @search = ''
                OR actor_user_name LIKE @search_like
                OR entity_type LIKE @search_like
                OR entity_number LIKE @search_like
                OR action_text LIKE @search_like
                OR result_text LIKE @search_like
                OR message_text LIKE @search_like
            ORDER BY logged_at DESC
            LIMIT @limit;
            """;

        var normalizedSearch = search?.Trim() ?? string.Empty;
        command.Parameters.AddWithValue("@search", normalizedSearch);
        command.Parameters.AddWithValue("@search_like", $"%{normalizedSearch}%");
        command.Parameters.AddWithValue("@limit", Math.Clamp(limit, 1, 1000));

        var rows = new List<WarehouseOperationLogRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadRecord(reader));
        }

        return rows;
    }

    private static WarehouseOperationLogRecord ReadRecord(MySqlDataReader reader)
    {
        var entityIdText = ReadString(reader, "entity_id");
        return new WarehouseOperationLogRecord(
            ReadGuid(reader, "id") ?? Guid.Empty,
            ReadDateTime(reader, "logged_at"),
            ReadString(reader, "actor_user_name"),
            ReadString(reader, "entity_type"),
            Guid.TryParse(entityIdText, out var entityId) ? entityId : null,
            ReadString(reader, "entity_number"),
            ReadString(reader, "action_text"),
            ReadString(reader, "result_text"),
            ReadString(reader, "message_text"));
    }

    private static string ReadString(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        if (reader.IsDBNull(ordinal))
        {
            return string.Empty;
        }

        return Convert.ToString(reader.GetValue(ordinal), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static Guid? ReadGuid(MySqlDataReader reader, string name)
    {
        var text = ReadString(reader, name);
        return Guid.TryParse(text, out var value) ? value : null;
    }

    private static DateTime ReadDateTime(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
    }
}
