using System.Globalization;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// INSERT в app_warehouse_operation_log.
// Колонки совпадают с тем что пишет Desktop'овский Backplane —
// единый формат журнала операций для UI/TSD.
public sealed class MySqlScanOperationLogger : IScanOperationLogger
{
    private readonly MySqlExecutor _executor;

    public MySqlScanOperationLogger(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task WriteAsync(ScanLogEntry entry, CancellationToken cancellationToken)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
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
                @entity_type,
                @entity_id,
                @entity_number,
                @action_text,
                @result_text,
                @message_text
            );
            """;

        command.Parameters.AddWithValue("@id", entry.Id == Guid.Empty ? Guid.NewGuid().ToString() : entry.Id.ToString());
        command.Parameters.AddWithValue("@actor_user_name", entry.ActorUserName ?? string.Empty);
        command.Parameters.AddWithValue("@entity_type", entry.EntityType ?? "Scan");
        command.Parameters.AddWithValue("@entity_id", entry.EntityId is null ? DBNull.Value : entry.EntityId.Value.ToString());
        command.Parameters.AddWithValue("@entity_number", entry.EntityNumber ?? string.Empty);
        command.Parameters.AddWithValue("@action_text", entry.ActionText ?? string.Empty);
        command.Parameters.AddWithValue("@result_text", entry.ResultText ?? string.Empty);
        command.Parameters.AddWithValue("@message_text", entry.MessageText ?? string.Empty);

        await command.ExecuteNonQueryAsync(cancellationToken);
        _ = CultureInfo.InvariantCulture;
    }
}
