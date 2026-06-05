using System.Globalization;
using System.Text.Json;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

public sealed class MySqlWarehouseStockOperationService : IWarehouseStockOperationService
{
    private readonly MySqlExecutor _executor;

    public MySqlWarehouseStockOperationService(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<StockTransferResult> TransferAsync(
        StockTransferRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return new StockTransferResult(false, 0, 0, "Количество перемещения должно быть больше нуля.");
        }

        if (request.SourceCellId == request.TargetCellId)
        {
            return new StockTransferResult(false, 0, 0, "Ячейка-источник совпадает с ячейкой-приёмником.");
        }

        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var source = await LoadLocationForUpdateAsync(
                connection, transaction, request.ItemId, request.SourceCellId, cancellationToken);
            if (source is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StockTransferResult(false, 0, 0, "Товар не найден в ячейке-источнике.");
            }

            var available = source.Quantity - source.ReservedQuantity;
            if (request.Quantity > available)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StockTransferResult(
                    false,
                    source.Quantity,
                    0,
                    $"Доступно к перемещению только {FormatQuantity(available)}; резерв {FormatQuantity(source.ReservedQuantity)}.");
            }

            var targetCell = await LoadCellAsync(connection, transaction, request.TargetCellId, cancellationToken);
            if (targetCell is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StockTransferResult(false, source.Quantity, 0, "Ячейка-приёмник не найдена.");
            }

            var target = await LoadLocationForUpdateAsync(
                connection, transaction, request.ItemId, request.TargetCellId, cancellationToken);

            var sourceAfter = source.Quantity - request.Quantity;
            var targetAfter = (target?.Quantity ?? 0m) + request.Quantity;

            await UpdateLocationQuantityAsync(
                connection, transaction, source.Id, sourceAfter, source.ReservedQuantity, cancellationToken);
            await UpsertTargetLocationAsync(
                connection, transaction, source, targetCell, targetAfter, target?.ReservedQuantity ?? 0m, cancellationToken);

            var documentId = Guid.NewGuid();
            var documentNumber = $"MOV-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            await InsertWarehouseDocumentAsync(
                connection,
                transaction,
                documentId,
                "transfer",
                "WMS: Перемещение между ячейками",
                documentNumber,
                "Проведен",
                source.WarehouseName,
                targetCell.WarehouseName,
                request.RelatedDocument,
                request.Comment,
                cancellationToken);
            await InsertWarehouseDocumentLineAsync(
                connection,
                transaction,
                documentId,
                "transfer",
                source.ItemCode,
                source.ItemName,
                request.Quantity,
                source.StorageCellCode,
                targetCell.Code,
                request.RelatedDocument,
                new
                {
                    source_quantity_before = source.Quantity,
                    source_quantity_after = sourceAfter,
                    target_quantity_before = target?.Quantity ?? 0m,
                    target_quantity_after = targetAfter
                },
                cancellationToken);
            await InsertOperationLogAsync(
                connection,
                transaction,
                request.Actor,
                "StockTransfer",
                documentId,
                documentNumber,
                "Перемещение между ячейками",
                "Успех",
                $"{source.ItemCode}: {source.StorageCellCode} -> {targetCell.Code}; количество {FormatQuantity(request.Quantity)}.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new StockTransferResult(
                true,
                sourceAfter,
                targetAfter,
                $"Перемещено {FormatQuantity(request.Quantity)}: {source.StorageCellCode} -> {targetCell.Code}.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<StockWriteOffResult> WriteOffAsync(
        StockWriteOffRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Quantity <= 0)
        {
            return new StockWriteOffResult(false, 0, string.Empty, "Количество списания должно быть больше нуля.");
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            return new StockWriteOffResult(false, 0, string.Empty, "Укажите причину списания.");
        }

        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            var source = await LoadLocationForUpdateAsync(
                connection, transaction, request.ItemId, request.SourceCellId, cancellationToken);
            if (source is null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StockWriteOffResult(false, 0, string.Empty, "Товар не найден в ячейке списания.");
            }

            var available = source.Quantity - source.ReservedQuantity;
            if (request.Quantity > available)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new StockWriteOffResult(
                    false,
                    source.Quantity,
                    string.Empty,
                    $"Доступно к списанию только {FormatQuantity(available)}; резерв {FormatQuantity(source.ReservedQuantity)}.");
            }

            var sourceAfter = source.Quantity - request.Quantity;
            await UpdateLocationQuantityAsync(
                connection, transaction, source.Id, sourceAfter, source.ReservedQuantity, cancellationToken);

            var documentId = Guid.NewGuid();
            var documentNumber = $"WOF-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            await InsertWarehouseDocumentAsync(
                connection,
                transaction,
                documentId,
                "write_off",
                "WMS: Списание по сборке",
                documentNumber,
                "Проведен",
                source.WarehouseName,
                source.WarehouseName,
                request.RelatedDocument,
                request.Comment ?? request.Reason,
                cancellationToken);
            await InsertWarehouseDocumentLineAsync(
                connection,
                transaction,
                documentId,
                "write_off",
                source.ItemCode,
                source.ItemName,
                request.Quantity,
                source.StorageCellCode,
                string.Empty,
                request.RelatedDocument,
                new
                {
                    reason = request.Reason,
                    quantity_before = source.Quantity,
                    quantity_after = sourceAfter
                },
                cancellationToken);
            await InsertOperationLogAsync(
                connection,
                transaction,
                request.Actor,
                "StockWriteOff",
                documentId,
                documentNumber,
                "Списание из ячейки по сборке",
                "Успех",
                $"{source.ItemCode}: {source.StorageCellCode}; количество {FormatQuantity(request.Quantity)}; причина: {request.Reason}.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new StockWriteOffResult(
                true,
                sourceAfter,
                documentNumber,
                $"Списано {FormatQuantity(request.Quantity)} из {source.StorageCellCode}. Документ {documentNumber}.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<CellInventoryCommitResult> CommitCellInventoryAsync(
        CellInventoryCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.Lines.Count == 0)
        {
            return FailInventory("В инвентаризации нет строк.");
        }

        var invalid = request.Lines.FirstOrDefault(line =>
            line.ActualQuantity < 0
            || line.ActualQuantity != line.SystemQuantity
            && (string.IsNullOrWhiteSpace(line.ResolutionCode) || string.IsNullOrWhiteSpace(line.Reason)));
        if (invalid is not null)
        {
            return FailInventory(
                invalid.ActualQuantity < 0
                    ? $"Фактическое количество {invalid.ItemCode} не может быть отрицательным."
                    : $"Расхождение по {invalid.ItemCode} не обосновано.");
        }

        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var line in request.Lines)
            {
                var current = await LoadLocationByIdForUpdateAsync(
                    connection, transaction, line.StockLocationId, cancellationToken);
                if (current is null || current.StorageCellId != request.CellId)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return FailInventory($"Позиция {line.ItemCode} больше не относится к ячейке {request.CellCode}.");
                }

                if (current.Quantity != line.SystemQuantity)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    return FailInventory(
                        $"Остаток {line.ItemCode} изменился во время пересчёта: было {FormatQuantity(line.SystemQuantity)}, стало {FormatQuantity(current.Quantity)}. Перезагрузите ячейку.");
                }
            }

            var documentId = Guid.NewGuid();
            var documentNumber = $"INV-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var differences = request.Lines.Where(line => line.ActualQuantity != line.SystemQuantity).ToArray();
            var shortage = differences.Sum(line => Math.Max(0, line.SystemQuantity - line.ActualQuantity));
            var surplus = differences.Sum(line => Math.Max(0, line.ActualQuantity - line.SystemQuantity));

            await InsertWarehouseDocumentAsync(
                connection,
                transaction,
                documentId,
                "inventory",
                "WMS: Инвентаризация ячейки",
                documentNumber,
                "Проведена",
                request.WarehouseName,
                request.WarehouseName,
                request.CellCode,
                request.Comment,
                cancellationToken);

            var lineNo = 0;
            foreach (var line in request.Lines)
            {
                lineNo++;
                await UpdateLocationQuantityAsync(
                    connection,
                    transaction,
                    line.StockLocationId,
                    line.ActualQuantity,
                    line.ReservedQuantity,
                    cancellationToken);
                await InsertWarehouseDocumentLineAsync(
                    connection,
                    transaction,
                    documentId,
                    "inventory",
                    line.ItemCode,
                    line.ItemName,
                    line.ActualQuantity,
                    request.CellCode,
                    request.CellCode,
                    null,
                    new
                    {
                        line_no = lineNo,
                        system_quantity = line.SystemQuantity,
                        actual_quantity = line.ActualQuantity,
                        difference = line.ActualQuantity - line.SystemQuantity,
                        resolution = line.ResolutionCode,
                        reason = line.Reason,
                        investigation_cell = line.InvestigationCellCode
                    },
                    cancellationToken,
                    lineNo);
            }

            await InsertOperationLogAsync(
                connection,
                transaction,
                request.Actor,
                "CellInventory",
                documentId,
                documentNumber,
                "Проведение инвентаризации ячейки",
                "Успех",
                $"Ячейка {request.CellCode}; строк {request.Lines.Count}; расхождений {differences.Length}; недостача {FormatQuantity(shortage)}; излишек {FormatQuantity(surplus)}.",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            return new CellInventoryCommitResult(
                true,
                documentId,
                documentNumber,
                request.Lines.Count,
                differences.Length,
                shortage,
                surplus,
                $"Инвентаризация {request.CellCode} проведена. Расхождений: {differences.Length}.");
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static async Task<LocationRow?> LoadLocationForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid itemId,
        Guid cellId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT id, item_id, item_code, item_name, warehouse_node_id, warehouse_name,
                   storage_cell_id, storage_cell_code, quantity, reserved_quantity
            FROM app_warehouse_stock_locations
            WHERE item_id = @item_id AND storage_cell_id = @cell_id
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@item_id", itemId.ToString());
        command.Parameters.AddWithValue("@cell_id", cellId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLocation(reader) : null;
    }

    private static async Task<LocationRow?> LoadLocationByIdForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT id, item_id, item_code, item_name, warehouse_node_id, warehouse_name,
                   storage_cell_id, storage_cell_code, quantity, reserved_quantity
            FROM app_warehouse_stock_locations
            WHERE id = @id
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadLocation(reader) : null;
    }

    private static async Task<CellRow?> LoadCellAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT id, warehouse_name, code
            FROM app_warehouse_storage_cells
            WHERE id = @id AND COALESCE(status_text, 'Активна') <> 'Закрыта'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new CellRow(
            ReadGuid(reader, "id"),
            ReadString(reader, "warehouse_name"),
            ReadString(reader, "code"));
    }

    private static async Task UpdateLocationQuantityAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id,
        decimal quantity,
        decimal reservedQuantity,
        CancellationToken cancellationToken)
    {
        if (quantity < reservedQuantity)
        {
            throw new InvalidOperationException(
                $"Новый остаток {FormatQuantity(quantity)} меньше резерва {FormatQuantity(reservedQuantity)}.");
        }

        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            UPDATE app_warehouse_stock_locations
            SET quantity = @quantity,
                last_movement_at_utc = UTC_TIMESTAMP(6),
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertTargetLocationAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        LocationRow source,
        CellRow targetCell,
        decimal quantity,
        decimal reservedQuantity,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO app_warehouse_stock_locations (
                id, item_id, item_code, item_name, warehouse_node_id, warehouse_name,
                storage_cell_id, storage_cell_code, quantity, reserved_quantity,
                last_movement_at_utc, created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @item_id, @item_code, @item_name, NULL, @warehouse_name,
                @cell_id, @cell_code, @quantity, @reserved_quantity,
                UTC_TIMESTAMP(6), UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            )
            ON DUPLICATE KEY UPDATE
                item_code = VALUES(item_code),
                item_name = VALUES(item_name),
                warehouse_name = VALUES(warehouse_name),
                storage_cell_code = VALUES(storage_cell_code),
                quantity = VALUES(quantity),
                reserved_quantity = VALUES(reserved_quantity),
                last_movement_at_utc = UTC_TIMESTAMP(6),
                updated_at_utc = UTC_TIMESTAMP(6);
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@item_id", source.ItemId.ToString());
        command.Parameters.AddWithValue("@item_code", source.ItemCode);
        command.Parameters.AddWithValue("@item_name", source.ItemName);
        command.Parameters.AddWithValue("@warehouse_name", targetCell.WarehouseName);
        command.Parameters.AddWithValue("@cell_id", targetCell.Id.ToString());
        command.Parameters.AddWithValue("@cell_code", targetCell.Code);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@reserved_quantity", reservedQuantity);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWarehouseDocumentAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid id,
        string kind,
        string type,
        string number,
        string status,
        string sourceWarehouse,
        string targetWarehouse,
        string? relatedDocument,
        string? comment,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO app_warehouse_documents (
                id, document_kind, document_type, number, document_date, status_text,
                source_warehouse, target_warehouse, related_document, comment_text,
                source_label, created_at_utc, updated_at_utc
            )
            VALUES (
                @id, @kind, @type, @number, UTC_TIMESTAMP(6), @status,
                @source_warehouse, @target_warehouse, @related_document, @comment,
                'Major WMS', UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
            );
            """;
        command.Parameters.AddWithValue("@id", id.ToString());
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@type", type);
        command.Parameters.AddWithValue("@number", number);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@source_warehouse", sourceWarehouse);
        command.Parameters.AddWithValue("@target_warehouse", targetWarehouse);
        command.Parameters.AddWithValue("@related_document", DbValue(relatedDocument));
        command.Parameters.AddWithValue("@comment", DbValue(comment));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertWarehouseDocumentLineAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid documentId,
        string kind,
        string itemCode,
        string itemName,
        decimal quantity,
        string sourceLocation,
        string targetLocation,
        string? relatedDocument,
        object fields,
        CancellationToken cancellationToken,
        int lineNo = 1)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO app_warehouse_document_lines (
                id, document_id, document_kind, line_no, item_code, item_name,
                quantity, unit_name, source_location, target_location,
                related_document, fields_json
            )
            VALUES (
                @id, @document_id, @kind, @line_no, @item_code, @item_name,
                @quantity, 'шт', @source_location, @target_location,
                @related_document, @fields_json
            );
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@document_id", documentId.ToString());
        command.Parameters.AddWithValue("@kind", kind);
        command.Parameters.AddWithValue("@line_no", lineNo);
        command.Parameters.AddWithValue("@item_code", itemCode);
        command.Parameters.AddWithValue("@item_name", itemName);
        command.Parameters.AddWithValue("@quantity", quantity);
        command.Parameters.AddWithValue("@source_location", sourceLocation);
        command.Parameters.AddWithValue("@target_location", targetLocation);
        command.Parameters.AddWithValue("@related_document", DbValue(relatedDocument));
        command.Parameters.AddWithValue("@fields_json", JsonSerializer.Serialize(fields));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertOperationLogAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string actor,
        string entityType,
        Guid entityId,
        string entityNumber,
        string action,
        string result,
        string message,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO app_warehouse_operation_log (
                id, logged_at, actor_user_name, entity_type, entity_id,
                entity_number, action_text, result_text, message_text
            )
            VALUES (
                @id, UTC_TIMESTAMP(6), @actor, @entity_type, @entity_id,
                @entity_number, @action, @result, @message
            );
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@actor", SafeActor(actor));
        command.Parameters.AddWithValue("@entity_type", entityType);
        command.Parameters.AddWithValue("@entity_id", entityId.ToString());
        command.Parameters.AddWithValue("@entity_number", entityNumber);
        command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@result", result);
        command.Parameters.AddWithValue("@message", message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static LocationRow ReadLocation(MySqlDataReader reader)
    {
        return new LocationRow(
            ReadGuid(reader, "id"),
            ReadGuid(reader, "item_id"),
            ReadString(reader, "item_code"),
            ReadString(reader, "item_name"),
            ReadString(reader, "warehouse_name"),
            ReadGuid(reader, "storage_cell_id"),
            ReadString(reader, "storage_cell_code"),
            ReadDecimal(reader, "quantity"),
            ReadDecimal(reader, "reserved_quantity"));
    }

    private static Guid ReadGuid(MySqlDataReader reader, string name)
    {
        return Guid.Parse(reader.GetValue(reader.GetOrdinal(name)).ToString()!);
    }

    private static string ReadString(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? string.Empty : reader.GetValue(ordinal)?.ToString() ?? string.Empty;
    }

    private static decimal ReadDecimal(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal)
            ? 0m
            : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static object DbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value.Trim();
    }

    private static string SafeActor(string actor)
    {
        return string.IsNullOrWhiteSpace(actor) ? "Кладовщик" : actor.Trim();
    }

    private static string FormatQuantity(decimal quantity)
    {
        return quantity.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU"));
    }

    private static CellInventoryCommitResult FailInventory(string message)
    {
        return new CellInventoryCommitResult(false, Guid.Empty, string.Empty, 0, 0, 0, 0, message);
    }

    private sealed record LocationRow(
        Guid Id,
        Guid ItemId,
        string ItemCode,
        string ItemName,
        string WarehouseName,
        Guid StorageCellId,
        string StorageCellCode,
        decimal Quantity,
        decimal ReservedQuantity);

    private sealed record CellRow(Guid Id, string WarehouseName, string Code);
}
