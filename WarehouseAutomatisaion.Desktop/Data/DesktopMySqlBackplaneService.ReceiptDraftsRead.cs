using System.Text.Json;
using MySqlConnector;
using WarehouseAutomatisaion.Application.Contracts.Receiving;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 8 (AI loop closure): чтение AI-распознанных черновиков из
// app_warehouse_documents где document_kind='receipt' и status_text in ('draft',…).
// Парсит fields_json чтобы достать supplier/invoice metadata из AI-распознавания.
//
// Pattern идентичный другим .ReceiptDrafts/.StockLocations partials — sync API
// в backplane, async wrapper в MySqlReceiptDraftReader.
public sealed partial class DesktopMySqlBackplaneService
{
    private const int MysqlReceiptReadCommandTimeoutSeconds = 30;

    public IReadOnlyList<ReceiptDraftSummary> LoadReceiptDrafts(string? statusFilter = "draft")
    {
        EnsureDatabaseAndSchema();

        // LEFT JOIN на строки чтобы посчитать lines_count + sum(quantity) — без отдельного round-trip.
        const string sql = """
            SELECT
                d.id, d.number, d.document_type, d.document_date, d.status_text,
                d.source_label, d.fields_json, d.created_at_utc,
                COUNT(l.id) AS lines_count,
                COALESCE(SUM(l.quantity), 0) AS total_qty
            FROM app_warehouse_documents d
            LEFT JOIN app_warehouse_document_lines l
                ON l.document_id = d.id AND l.document_kind = 'receipt'
            WHERE d.document_kind = 'receipt'
              AND (@status_filter IS NULL OR d.status_text = @status_filter)
            GROUP BY d.id, d.number, d.document_type, d.document_date, d.status_text,
                     d.source_label, d.fields_json, d.created_at_utc
            ORDER BY d.created_at_utc DESC
            LIMIT 500;
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlReceiptReadCommandTimeoutSeconds);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = MysqlReceiptReadCommandTimeoutSeconds;
        cmd.Parameters.AddWithValue("@status_filter", (object?)statusFilter ?? DBNull.Value);

        var results = new List<ReceiptDraftSummary>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var id = ReadGuid(reader, 0);
            var number = ReadString(reader, 1);
            var documentType = ReadString(reader, 2);
            var documentDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);
            var statusText = ReadString(reader, 4);
            var sourceLabel = ReadString(reader, 5);
            var fieldsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            var createdAtUtc = reader.GetDateTime(7);
            var linesCount = reader.GetInt32(8);
            var totalQty = reader.GetDecimal(9);

            var (supplierName, supplierTaxId, invoiceNumber, totalAmount) = ParseFieldsJsonHeader(fieldsJson);

            results.Add(new ReceiptDraftSummary(
                Id: id,
                Number: number,
                DocumentType: documentType,
                DocumentDate: documentDate,
                StatusText: statusText,
                SupplierName: supplierName ?? "—",
                SupplierTaxId: supplierTaxId,
                InvoiceNumber: invoiceNumber ?? number,
                TotalAmount: totalAmount,
                LinesCount: linesCount,
                TotalQuantity: totalQty,
                SourceLabel: sourceLabel,
                CreatedAtUtc: createdAtUtc));
        }
        return results;
    }

    public ReceiptDraftDetail? LoadReceiptDraftDetail(Guid id)
    {
        EnsureDatabaseAndSchema();

        const string headerSql = """
            SELECT
                d.id, d.number, d.document_type, d.document_date, d.status_text,
                d.source_label, d.fields_json, d.created_at_utc,
                (SELECT COUNT(*) FROM app_warehouse_document_lines l
                  WHERE l.document_id = d.id AND l.document_kind = 'receipt') AS lines_count,
                COALESCE((SELECT SUM(l.quantity) FROM app_warehouse_document_lines l
                  WHERE l.document_id = d.id AND l.document_kind = 'receipt'), 0) AS total_qty
            FROM app_warehouse_documents d
            WHERE d.document_kind = 'receipt' AND d.id = @id
            LIMIT 1;
            """;

        const string linesSql = """
            SELECT id, line_no, item_code, item_name, quantity, unit_name, fields_json
            FROM app_warehouse_document_lines
            WHERE document_id = @document_id AND document_kind = 'receipt'
            ORDER BY line_no;
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlReceiptReadCommandTimeoutSeconds);

        ReceiptDraftSummary? header = null;
        using (var headerCmd = connection.CreateCommand())
        {
            headerCmd.CommandText = headerSql;
            headerCmd.Parameters.AddWithValue("@id", id.ToString());
            using var reader = headerCmd.ExecuteReader();
            if (reader.Read())
            {
                var headerId = ReadGuid(reader, 0);
                var number = ReadString(reader, 1);
                var documentType = ReadString(reader, 2);
                var documentDate = reader.IsDBNull(3) ? DateTime.MinValue : reader.GetDateTime(3);
                var statusText = ReadString(reader, 4);
                var sourceLabel = ReadString(reader, 5);
                var fieldsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                var createdAtUtc = reader.GetDateTime(7);
                var linesCount = reader.GetInt32(8);
                var totalQty = reader.GetDecimal(9);
                var (supplierName, supplierTaxId, invoiceNumber, totalAmount) = ParseFieldsJsonHeader(fieldsJson);

                header = new ReceiptDraftSummary(
                    Id: headerId,
                    Number: number,
                    DocumentType: documentType,
                    DocumentDate: documentDate,
                    StatusText: statusText,
                    SupplierName: supplierName ?? "—",
                    SupplierTaxId: supplierTaxId,
                    InvoiceNumber: invoiceNumber ?? number,
                    TotalAmount: totalAmount,
                    LinesCount: linesCount,
                    TotalQuantity: totalQty,
                    SourceLabel: sourceLabel,
                    CreatedAtUtc: createdAtUtc);
            }
        }

        if (header is null)
        {
            return null;
        }

        var lines = new List<ReceiptDraftLineDetail>();
        using (var linesCmd = connection.CreateCommand())
        {
            linesCmd.CommandText = linesSql;
            linesCmd.Parameters.AddWithValue("@document_id", id.ToString());
            using var reader = linesCmd.ExecuteReader();
            while (reader.Read())
            {
                var lineId = ReadGuid(reader, 0);
                var lineNo = reader.GetInt32(1);
                var itemCode = reader.IsDBNull(2) ? null : reader.GetString(2);
                var itemName = ReadString(reader, 3);
                var qty = reader.GetDecimal(4);
                var unit = reader.IsDBNull(5) ? null : reader.GetString(5);
                var fieldsJson = reader.IsDBNull(6) ? null : reader.GetString(6);
                var (matchedItemId, unitPrice, subtotal, total) = ParseFieldsJsonLine(fieldsJson);

                lines.Add(new ReceiptDraftLineDetail(
                    Id: lineId,
                    LineNumber: lineNo,
                    MatchedItemId: matchedItemId,
                    OriginalItemName: itemName,
                    OriginalSku: itemCode,
                    Unit: unit,
                    Quantity: qty,
                    UnitPrice: unitPrice,
                    Subtotal: subtotal,
                    Total: total));
            }
        }

        return new ReceiptDraftDetail(header, lines);
    }

    public void MarkReceiptDraftReceived(Guid draftId, Guid receivingCellId, string receivingCellCode, int linesReceived)
    {
        EnsureDatabaseAndSchema();

        // Сохраняем receiving метаданные в fields_json без потери исходных AI-данных.
        // Читаем текущий JSON, мерджим, пишем обратно.
        const string readSql = "SELECT fields_json FROM app_warehouse_documents WHERE id = @id LIMIT 1;";
        const string updateSql = """
            UPDATE app_warehouse_documents
            SET status_text = 'received',
                fields_json = @fields_json,
                updated_at_utc = UTC_TIMESTAMP(6)
            WHERE id = @id;
            """;

        using var connection = DesktopMySqlCommandRunner.CreateOpenConnection(
            _options, useDatabase: true, MysqlConnectTimeoutSeconds, MysqlReceiptReadCommandTimeoutSeconds);

        string? currentJson = null;
        using (var readCmd = connection.CreateCommand())
        {
            readCmd.CommandText = readSql;
            readCmd.Parameters.AddWithValue("@id", draftId.ToString());
            using var reader = readCmd.ExecuteReader();
            if (reader.Read() && !reader.IsDBNull(0))
            {
                currentJson = reader.GetString(0);
            }
        }

        // Мерджим: достаём существующий объект, доливаем receiving поля.
        Dictionary<string, object?> merged = new();
        if (!string.IsNullOrWhiteSpace(currentJson))
        {
            try
            {
                using var doc = JsonDocument.Parse(currentJson);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    merged[prop.Name] = prop.Value.ValueKind switch
                    {
                        JsonValueKind.String => prop.Value.GetString(),
                        JsonValueKind.Number => prop.Value.TryGetInt64(out var l) ? (object?)l : prop.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => prop.Value.GetRawText()
                    };
                }
            }
            catch (JsonException)
            {
                // если был мусор — игнорируем и пишем заново
            }
        }

        merged["receiving_cell_id"] = receivingCellId.ToString();
        merged["receiving_cell_code"] = receivingCellCode;
        merged["receiving_lines_count"] = linesReceived;
        merged["received_at_utc"] = DateTime.UtcNow.ToString("o");

        var fieldsJson = JsonSerializer.Serialize(merged);

        using (var updateCmd = connection.CreateCommand())
        {
            updateCmd.CommandText = updateSql;
            updateCmd.Parameters.AddWithValue("@id", draftId.ToString());
            updateCmd.Parameters.AddWithValue("@fields_json", fieldsJson);
            updateCmd.ExecuteNonQuery();
        }
    }

    // ===== JSON helpers =====

    private static (string? supplierName, string? taxId, string? invoiceNumber, decimal? totalAmount)
        ParseFieldsJsonHeader(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string? Supplier = TryGetString(root, "supplier_name");
            string? Tax = TryGetString(root, "supplier_tax_id");
            string? Invoice = TryGetString(root, "invoice_number");
            decimal? Total = TryGetDecimal(root, "total_amount");
            return (Supplier, Tax, Invoice, Total);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static (Guid? matchedItemId, decimal? unitPrice, decimal? subtotal, decimal? total)
        ParseFieldsJsonLine(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, null, null, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var matchedStr = TryGetString(root, "matched_item_id");
            Guid? matched = string.IsNullOrWhiteSpace(matchedStr) ? null :
                (Guid.TryParse(matchedStr, out var g) ? g : (Guid?)null);
            return (
                matched,
                TryGetDecimal(root, "unit_price"),
                TryGetDecimal(root, "subtotal"),
                TryGetDecimal(root, "total"));
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }

    private static string? TryGetString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return null;
        }
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.ToString(),
            JsonValueKind.Null => null,
            _ => prop.ToString()
        };
    }

    private static decimal? TryGetDecimal(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var prop))
        {
            return null;
        }
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var d))
        {
            return d;
        }
        if (prop.ValueKind == JsonValueKind.String
            && decimal.TryParse(prop.GetString(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var sd))
        {
            return sd;
        }
        return null;
    }

    private static Guid ReadGuid(MySqlDataReader reader, int ord)
    {
        // MySqlConnector auto-конвертирует CHAR(36) в Guid → GetString() падает.
        // Грабли описаны в CLAUDE.md в секции «Конвенции».
        var raw = reader.GetValue(ord);
        return raw switch
        {
            Guid g => g,
            string s => Guid.TryParse(s, out var g2) ? g2 : Guid.Empty,
            _ => Guid.Empty
        };
    }

    private static string ReadString(MySqlDataReader reader, int ord)
        => reader.IsDBNull(ord) ? string.Empty : reader.GetValue(ord)?.ToString() ?? string.Empty;
}
