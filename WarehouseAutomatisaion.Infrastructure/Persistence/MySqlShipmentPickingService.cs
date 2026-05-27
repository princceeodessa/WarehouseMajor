using System.Globalization;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

// Реализация TSD-сборки отгрузок. Source: app_sales_documents kind='shipment'.
// Прогресс — JOIN на app_warehouse_documents где document_type='ТСД: Сборка'
// и related_document = sales.number.
//
// CompleteDocumentAsync — атомарный flow:
//   SELECT FOR UPDATE → load lines/picked → validate → UPDATE ×2 → INSERT ×2 → COMMIT.
public sealed class MySqlShipmentPickingService : IShipmentPickingService
{
    private readonly MySqlExecutor _executor;

    public MySqlShipmentPickingService(MySqlExecutor executor)
    {
        _executor = executor;
    }

    public async Task<IReadOnlyList<ShipmentPickingDocumentSummary>> GetDocumentsAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);

        var headers = await LoadDocumentHeadersAsync(connection, Math.Clamp(limit, 1, 50), cancellationToken);
        if (headers.Count == 0)
        {
            return Array.Empty<ShipmentPickingDocumentSummary>();
        }

        var summaries = new List<ShipmentPickingDocumentSummary>(headers.Count);
        foreach (var header in headers)
        {
            var lines = await LoadDocumentLinesAsync(connection, header.DocumentId, cancellationToken);
            var picked = await LoadPickedBySkuAsync(connection, header.Number, cancellationToken);
            summaries.Add(BuildSummary(header, lines, picked));
        }

        return summaries;
    }

    public async Task<ShipmentPickingDocumentDetails?> GetDocumentAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);

        var header = await LoadDocumentHeaderAsync(connection, documentId, cancellationToken);
        if (header is null)
        {
            return null;
        }

        var lines = await LoadDocumentLinesAsync(connection, documentId, cancellationToken);
        var picked = await LoadPickedBySkuAsync(connection, header.Number, cancellationToken);
        return BuildDetails(header, lines, picked);
    }

    public async Task<ShipmentPickingCompletionResult> CompleteDocumentAsync(
        Guid documentId,
        string worker,
        CancellationToken cancellationToken)
    {
        await using var connection = await _executor.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var header = await LoadDocumentHeaderForUpdateAsync(connection, transaction, documentId, cancellationToken);
        if (header is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail(documentId, string.Empty, "Задание сборки не найдено.");
        }

        var lines = await LoadDocumentLinesAsync(connection, documentId, cancellationToken, transaction);
        var picked = await LoadPickedBySkuAsync(connection, header.Number, cancellationToken, transaction);
        var details = BuildDetails(header, lines, picked);

        if (details.LineCount == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return Fail(documentId, header.Number, "В отгрузке нет строк для сборки.");
        }

        if (details.RemainingQuantity > 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new ShipmentPickingCompletionResult(
                false,
                documentId,
                header.Number,
                header.Status,
                details.RequiredQuantity,
                details.PickedQuantity,
                details.RemainingQuantity,
                $"Сборка не завершена: осталось {FormatQuantity(details.RemainingQuantity)} шт.");
        }

        await MarkTsdDocumentsReadyAsync(connection, transaction, header.Number, cancellationToken);
        await MarkShipmentReadyAsync(connection, transaction, documentId, cancellationToken);
        await WriteSalesCompletionLogAsync(connection, transaction, details, worker, cancellationToken);
        await WriteWarehouseCompletionLogAsync(connection, transaction, details, worker, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new ShipmentPickingCompletionResult(
            true,
            documentId,
            header.Number,
            "Готова к отгрузке",
            details.RequiredQuantity,
            details.PickedQuantity,
            details.RemainingQuantity,
            $"Сборка {header.Number} завершена: {FormatQuantity(details.PickedQuantity)} шт.");
    }

    private static async Task<List<DocumentHeader>> LoadDocumentHeadersAsync(
        MySqlConnection connection,
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT
                id,
                number,
                document_date,
                customer_name,
                warehouse_name,
                status_text
            FROM app_sales_documents
            WHERE document_kind = 'shipment'
                AND COALESCE(status_text, '') NOT IN ('Отгружена', 'Отменена')
            ORDER BY
                CASE COALESCE(status_text, '')
                    WHEN 'К сборке' THEN 0
                    WHEN 'Черновик' THEN 1
                    WHEN 'Готова к отгрузке' THEN 2
                    ELSE 3
                END,
                document_date DESC,
                number DESC
            LIMIT @limit;
            """;
        command.Parameters.AddWithValue("@limit", limit);

        var rows = new List<DocumentHeader>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(ReadHeader(reader));
        }

        return rows;
    }

    private static async Task<DocumentHeader?> LoadDocumentHeaderAsync(
        MySqlConnection connection,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT id, number, document_date, customer_name, warehouse_name, status_text
            FROM app_sales_documents
            WHERE id = @id AND document_kind = 'shipment'
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("@id", documentId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHeader(reader) : null;
    }

    private static async Task<DocumentHeader?> LoadDocumentHeaderForUpdateAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT id, number, document_date, customer_name, warehouse_name, status_text
            FROM app_sales_documents
            WHERE id = @id AND document_kind = 'shipment'
            LIMIT 1
            FOR UPDATE;
            """;
        command.Parameters.AddWithValue("@id", documentId.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadHeader(reader) : null;
    }

    private static async Task<IReadOnlyList<DocumentLineSource>> LoadDocumentLinesAsync(
        MySqlConnection connection,
        Guid documentId,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT line_no, item_code, item_name, unit_name, quantity
            FROM app_sales_document_lines
            WHERE document_id = @document_id
            ORDER BY line_no;
            """;
        command.Parameters.AddWithValue("@document_id", documentId.ToString());

        var rows = new List<DocumentLineSource>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new DocumentLineSource(
                ReadInt32(reader, "line_no"),
                ScanValueNormalizer.ReadString(reader, "item_code"),
                ScanValueNormalizer.ReadString(reader, "item_name"),
                ScanValueNormalizer.ReadString(reader, "unit_name"),
                ReadDecimal(reader, "quantity")));
        }

        return rows;
    }

    private static async Task<Dictionary<string, decimal>> LoadPickedBySkuAsync(
        MySqlConnection connection,
        string documentNumber,
        CancellationToken cancellationToken,
        MySqlTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            SELECT line.item_code, SUM(line.quantity) AS picked_quantity
            FROM app_warehouse_document_lines line
            INNER JOIN app_warehouse_documents document ON document.id = line.document_id
            WHERE document.source_label = 'TSD'
                AND document.document_kind = 'transfer'
                AND document.document_type = 'ТСД: Сборка'
                AND document.related_document = @related_document
                AND line.item_code IS NOT NULL
                AND line.item_code <> ''
            GROUP BY line.item_code;
            """;
        command.Parameters.AddWithValue("@related_document", documentNumber);

        var rows = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var itemCode = ScanValueNormalizer.ReadString(reader, "item_code");
            if (!string.IsNullOrWhiteSpace(itemCode))
            {
                rows[itemCode] = ReadDecimal(reader, "picked_quantity");
            }
        }

        return rows;
    }

    private static async Task MarkTsdDocumentsReadyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        string documentNumber,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            UPDATE app_warehouse_documents
            SET status_text = 'Готово ТСД', updated_at_utc = UTC_TIMESTAMP(6)
            WHERE source_label = 'TSD'
                AND document_kind = 'transfer'
                AND document_type = 'ТСД: Сборка'
                AND related_document = @related_document;
            """;
        command.Parameters.AddWithValue("@related_document", documentNumber);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task MarkShipmentReadyAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            UPDATE app_sales_documents
            SET status_text = 'Готова к отгрузке', updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id AND document_kind = 'shipment';
            """;
        command.Parameters.AddWithValue("@id", documentId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteSalesCompletionLogAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ShipmentPickingDocumentDetails details,
        string worker,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandTimeout = MySqlExecutor.DefaultCommandTimeoutSeconds;
        command.CommandText = """
            INSERT INTO app_sales_operation_log (
                id, logged_at, actor_user_name, entity_type, entity_id,
                entity_number, action_text, result_text, message_text
            )
            VALUES (
                @id, UTC_TIMESTAMP(6), @actor, 'SalesShipment', @entity_id,
                @entity_number, 'TSD завершение сборки', 'Успех', @message
            );
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@actor", SafeWorker(worker));
        command.Parameters.AddWithValue("@entity_id", details.DocumentId.ToString());
        command.Parameters.AddWithValue("@entity_number", details.Number);
        command.Parameters.AddWithValue("@message", BuildCompletionMessage(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task WriteWarehouseCompletionLogAsync(
        MySqlConnection connection,
        MySqlTransaction transaction,
        ShipmentPickingDocumentDetails details,
        string worker,
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
                @id, UTC_TIMESTAMP(6), @actor, 'SalesShipment', @entity_id,
                @entity_number, 'TSD завершение сборки', 'Успех', @message
            );
            """;
        command.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
        command.Parameters.AddWithValue("@actor", SafeWorker(worker));
        command.Parameters.AddWithValue("@entity_id", details.DocumentId.ToString());
        command.Parameters.AddWithValue("@entity_number", details.Number);
        command.Parameters.AddWithValue("@message", BuildCompletionMessage(details));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static ShipmentPickingDocumentSummary BuildSummary(
        DocumentHeader header,
        IReadOnlyList<DocumentLineSource> lines,
        IReadOnlyDictionary<string, decimal> pickedBySku)
    {
        var required = lines.Sum(line => line.Quantity);
        var picked = lines.Sum(line => pickedBySku.GetValueOrDefault(line.ItemCode));
        return new ShipmentPickingDocumentSummary(
            header.DocumentId,
            header.Number,
            header.DocumentDate,
            header.CustomerName,
            header.WarehouseName,
            header.Status,
            lines.Count,
            required,
            picked,
            Math.Max(0, required - picked));
    }

    private static ShipmentPickingDocumentDetails BuildDetails(
        DocumentHeader header,
        IReadOnlyList<DocumentLineSource> sourceLines,
        IReadOnlyDictionary<string, decimal> pickedBySku)
    {
        var lines = sourceLines
            .Select(line =>
            {
                var picked = pickedBySku.GetValueOrDefault(line.ItemCode);
                var remaining = Math.Max(0, line.Quantity - picked);
                var status = remaining <= 0 ? "done" : picked > 0 ? "partial" : "pending";
                return new ShipmentPickingDocumentLine(
                    line.LineNo, line.ItemCode, line.ItemName, line.UnitName,
                    line.Quantity, picked, remaining, status);
            })
            .ToArray();

        var required = lines.Sum(l => l.RequiredQuantity);
        var pickedTotal = lines.Sum(l => l.PickedQuantity);

        return new ShipmentPickingDocumentDetails(
            header.DocumentId, header.Number, header.DocumentDate, header.CustomerName,
            header.WarehouseName, header.Status, lines.Length, required, pickedTotal,
            Math.Max(0, required - pickedTotal), lines);
    }

    private static DocumentHeader ReadHeader(MySqlDataReader reader)
    {
        return new DocumentHeader(
            ScanValueNormalizer.ReadGuid(reader, "id"),
            ScanValueNormalizer.ReadString(reader, "number"),
            ReadDateTime(reader, "document_date"),
            ScanValueNormalizer.ReadString(reader, "customer_name"),
            ScanValueNormalizer.ReadString(reader, "warehouse_name"),
            ScanValueNormalizer.ReadString(reader, "status_text"));
    }

    private static DateTime ReadDateTime(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? DateTime.MinValue : reader.GetDateTime(ordinal);
    }

    private static int ReadInt32(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToInt32(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static decimal ReadDecimal(MySqlDataReader reader, string name)
    {
        var ordinal = reader.GetOrdinal(name);
        return reader.IsDBNull(ordinal) ? 0 : Convert.ToDecimal(reader.GetValue(ordinal), CultureInfo.InvariantCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string SafeWorker(string worker)
    {
        return string.IsNullOrWhiteSpace(worker) ? "Кладовщик" : worker.Trim();
    }

    private static string BuildCompletionMessage(ShipmentPickingDocumentDetails details)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"Собрано {FormatQuantity(details.PickedQuantity)} из {FormatQuantity(details.RequiredQuantity)} шт.; клиент={details.CustomerName}; склад={details.WarehouseName}");
    }

    private static ShipmentPickingCompletionResult Fail(Guid documentId, string number, string message)
    {
        return new ShipmentPickingCompletionResult(false, documentId, number, string.Empty, 0, 0, 0, message);
    }

    private sealed record DocumentHeader(
        Guid DocumentId,
        string Number,
        DateTime DocumentDate,
        string CustomerName,
        string WarehouseName,
        string Status);

    private sealed record DocumentLineSource(
        int LineNo,
        string ItemCode,
        string ItemName,
        string UnitName,
        decimal Quantity);
}
