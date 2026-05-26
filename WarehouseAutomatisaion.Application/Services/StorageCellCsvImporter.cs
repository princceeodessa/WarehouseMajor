using System.Globalization;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 3 Task 21: парсер CSV для массового импорта ячеек.
// Поддерживает разделители ; (русская традиция, Excel default в ru-RU) и , (Excel international).
// Заголовок обязателен. Колонки нечувствительны к регистру и порядку.
//
// Минимальный набор колонок: warehouse_name, code.
// Дополнительные: zone_code, zone_name, row_no, rack_no, shelf_no, cell_no, cell_type,
// capacity, status_text, comment_text.
//
// Пустые значения колонок (для chars-typed полей) → null, для int → 0, для decimal → 0.
public sealed class StorageCellCsvImporter
{
    public ImportResult Parse(string csvContent)
    {
        if (string.IsNullOrWhiteSpace(csvContent))
        {
            return new ImportResult(
                Array.Empty<StorageCellRequest>(),
                new[] { "Файл пустой." });
        }

        var lines = csvContent.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
        if (lines.Length < 2)
        {
            return new ImportResult(
                Array.Empty<StorageCellRequest>(),
                new[] { "Файл должен содержать заголовок и хотя бы одну строку данных." });
        }

        // Определяем разделитель — пробуем `;`, иначе `,`.
        var separator = DetectSeparator(lines[0]);

        // Парсим заголовок.
        var header = SplitCsvLine(lines[0], separator);
        var columnMap = BuildColumnMap(header);

        if (!columnMap.ContainsKey("warehouse_name") || !columnMap.ContainsKey("code"))
        {
            return new ImportResult(
                Array.Empty<StorageCellRequest>(),
                new[] { "Обязательные колонки 'warehouse_name' и 'code' не найдены в заголовке. Доступные колонки: " + string.Join(", ", header) });
        }

        var requests = new List<StorageCellRequest>();
        var errors = new List<string>();

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var fields = SplitCsvLine(line, separator);
            try
            {
                var request = BuildRequest(fields, columnMap, i + 1);
                requests.Add(request);
            }
            catch (Exception exception)
            {
                errors.Add($"Строка {i + 1}: {exception.Message}");
            }
        }

        return new ImportResult(requests, errors);
    }

    private static char DetectSeparator(string headerLine)
    {
        var semicolons = headerLine.Count(c => c == ';');
        var commas = headerLine.Count(c => c == ',');
        return semicolons >= commas ? ';' : ',';
    }

    private static List<string> SplitCsvLine(string line, char separator)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var insideQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')
            {
                insideQuotes = !insideQuotes;
                continue;
            }

            if (ch == separator && !insideQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
                continue;
            }

            current.Append(ch);
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Count; i++)
        {
            var key = NormalizeColumnName(header[i]);
            if (!string.IsNullOrEmpty(key) && !map.ContainsKey(key))
            {
                map[key] = i;
            }
        }
        return map;
    }

    private static string NormalizeColumnName(string raw)
    {
        return raw.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_').Trim('"');
    }

    private static StorageCellRequest BuildRequest(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> columnMap,
        int rowNumber)
    {
        var warehouseName = GetString(fields, columnMap, "warehouse_name");
        var code = GetString(fields, columnMap, "code");

        if (string.IsNullOrEmpty(warehouseName))
        {
            throw new InvalidOperationException("Не указан склад (warehouse_name).");
        }

        if (string.IsNullOrEmpty(code))
        {
            throw new InvalidOperationException("Не указан код ячейки (code).");
        }

        return new StorageCellRequest(
            Code: code!,
            WarehouseNodeId: null,
            WarehouseName: warehouseName!,
            ZoneCode: GetString(fields, columnMap, "zone_code"),
            ZoneName: GetString(fields, columnMap, "zone_name"),
            RowNo: GetInt(fields, columnMap, "row_no", "row"),
            RackNo: GetInt(fields, columnMap, "rack_no", "rack"),
            ShelfNo: GetInt(fields, columnMap, "shelf_no", "shelf"),
            CellNo: GetInt(fields, columnMap, "cell_no", "cell"),
            CellType: GetString(fields, columnMap, "cell_type", "type") ?? CellTypes.Storage,
            Capacity: GetDecimal(fields, columnMap, "capacity"),
            StatusText: GetString(fields, columnMap, "status_text", "status") ?? CellStatuses.Active,
            CommentText: GetString(fields, columnMap, "comment_text", "comment"));
    }

    private static string? GetString(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> columnMap,
        params string[] columnNames)
    {
        foreach (var name in columnNames)
        {
            if (columnMap.TryGetValue(name, out var index) && index < fields.Count)
            {
                var value = fields[index]?.Trim();
                if (!string.IsNullOrEmpty(value))
                {
                    return value;
                }
            }
        }
        return null;
    }

    private static int GetInt(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> columnMap,
        params string[] columnNames)
    {
        var raw = GetString(fields, columnMap, columnNames);
        if (string.IsNullOrEmpty(raw))
        {
            return 0;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Поле '{columnNames[0]}' должно быть целым числом, получено: '{raw}'");
    }

    private static decimal GetDecimal(
        IReadOnlyList<string> fields,
        IReadOnlyDictionary<string, int> columnMap,
        params string[] columnNames)
    {
        var raw = GetString(fields, columnMap, columnNames);
        if (string.IsNullOrEmpty(raw))
        {
            return 0m;
        }

        var normalized = raw.Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw new InvalidOperationException($"Поле '{columnNames[0]}' должно быть числом, получено: '{raw}'");
    }
}

public sealed record ImportResult(
    IReadOnlyList<StorageCellRequest> Requests,
    IReadOnlyList<string> Errors);
