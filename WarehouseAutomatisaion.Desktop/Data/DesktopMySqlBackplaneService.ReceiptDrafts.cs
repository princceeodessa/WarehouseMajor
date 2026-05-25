using System.Text.Json;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5: запись черновика приёмочного документа (AI-распознанная накладная)
// в app_warehouse_documents с document_kind='receipt' + app_warehouse_document_lines.
// Транзакционно (rollback если упала любая строка).
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlReceiptCommandTimeoutSeconds = 30;

    public Guid CreateReceiptDraft(ReceiptDraft draft)
    {
        EnsureDatabaseAndSchema();
        EnsureUserProfile(draft.CreatedByActor);

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options,
            useDatabase: true,
            MysqlConnectTimeoutSeconds,
            MysqlReceiptCommandTimeoutSeconds);

        using var transaction = connection.BeginTransaction();

        try
        {
            var documentId = Guid.NewGuid();

            // Заголовок документа: используем поле fields_json для AI-метаданных
            // (поставщик, ИНН, итоги) — это упрощает миграцию схемы под новые поля.
            var fieldsJson = JsonSerializer.Serialize(new
            {
                supplier_name = draft.SupplierName,
                supplier_tax_id = draft.SupplierTaxId,
                invoice_number = draft.InvoiceNumber,
                invoice_date = draft.InvoiceDate?.ToString("yyyy-MM-dd"),
                currency = draft.Currency,
                total_amount = draft.TotalAmount,
                total_vat = draft.TotalVat,
                source = draft.SourceLabel
            });

            const string headerSql = """
                INSERT INTO app_warehouse_documents (
                    id, document_kind, document_type, number, document_date,
                    status_text, source_warehouse, target_warehouse, related_document,
                    comment_text, source_label, fields_json,
                    created_at_utc, updated_at_utc
                )
                VALUES (
                    @id, 'receipt', @document_type, @number, @document_date,
                    'draft', NULL, NULL, NULL,
                    @comment_text, @source_label, @fields_json,
                    UTC_TIMESTAMP(6), UTC_TIMESTAMP(6)
                );
                """;

            using (var headerCmd = connection.CreateCommand())
            {
                headerCmd.Transaction = transaction;
                headerCmd.CommandText = headerSql;
                headerCmd.CommandTimeout = MysqlReceiptCommandTimeoutSeconds;
                headerCmd.Parameters.AddWithValue("@id", documentId.ToString());
                headerCmd.Parameters.AddWithValue("@document_type", "Приёмка (AI)");
                headerCmd.Parameters.AddWithValue("@number", draft.InvoiceNumber ?? $"AI-{DateTime.UtcNow:yyyyMMdd-HHmmss}");
                headerCmd.Parameters.AddWithValue("@document_date", (object?)draft.InvoiceDate?.ToDateTime(TimeOnly.MinValue) ?? DateTime.UtcNow);
                headerCmd.Parameters.AddWithValue("@comment_text", (object?)draft.CommentText ?? DBNull.Value);
                headerCmd.Parameters.AddWithValue("@source_label", draft.SourceLabel);
                headerCmd.Parameters.AddWithValue("@fields_json", fieldsJson);
                headerCmd.ExecuteNonQuery();
            }

            // Строки документа
            const string lineSql = """
                INSERT INTO app_warehouse_document_lines (
                    id, document_id, document_kind, line_no,
                    item_code, item_name, quantity, unit_name,
                    source_location, target_location, related_document, fields_json
                )
                VALUES (
                    @id, @document_id, 'receipt', @line_no,
                    @item_code, @item_name, @quantity, @unit_name,
                    NULL, NULL, NULL, @fields_json
                );
                """;

            foreach (var line in draft.Lines)
            {
                var lineFieldsJson = JsonSerializer.Serialize(new
                {
                    matched_item_id = line.MatchedItemId?.ToString(),
                    original_sku = line.OriginalSku,
                    unit_price = line.UnitPrice,
                    vat = line.Vat,
                    subtotal = line.Subtotal,
                    total = line.Total
                });

                using var lineCmd = connection.CreateCommand();
                lineCmd.Transaction = transaction;
                lineCmd.CommandText = lineSql;
                lineCmd.CommandTimeout = MysqlReceiptCommandTimeoutSeconds;
                lineCmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
                lineCmd.Parameters.AddWithValue("@document_id", documentId.ToString());
                lineCmd.Parameters.AddWithValue("@line_no", line.LineNumber);
                lineCmd.Parameters.AddWithValue("@item_code", (object?)line.OriginalSku ?? DBNull.Value);
                lineCmd.Parameters.AddWithValue("@item_name", line.OriginalItemName);
                lineCmd.Parameters.AddWithValue("@quantity", line.Quantity);
                lineCmd.Parameters.AddWithValue("@unit_name", (object?)line.Unit ?? DBNull.Value);
                lineCmd.Parameters.AddWithValue("@fields_json", lineFieldsJson);
                lineCmd.ExecuteNonQuery();
            }

            transaction.Commit();
            return documentId;
        }
        catch
        {
            try
            {
                transaction.Rollback();
            }
            catch (MySqlException)
            {
                // best-effort rollback — лог не нужен, ошибка уже выше пробрасывается
            }
            throw;
        }
    }
}
