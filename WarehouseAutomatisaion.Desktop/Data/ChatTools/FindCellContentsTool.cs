using System.Text.Json;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data.ChatTools;

// Sprint 12: tool «что в ячейке».
// Принимает code ячейки (например WH02-PCK-01-01-01-01) → resolve в id → читаем содержимое.
public sealed class FindCellContentsTool : IChatTool
{
    private readonly IStorageCellCatalog _cellCatalog;
    private readonly IStockLocationRepository _stockLocations;

    public FindCellContentsTool(IStorageCellCatalog cellCatalog, IStockLocationRepository stockLocations)
    {
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
    }

    public string Name => "find_cell_contents";

    public string Description => """
        Показать что лежит в указанной ячейке склада. Вызывай когда оператор спрашивает
        «что в ячейке X?», «покажи содержимое ячейки Y». На вход — код ячейки (точно
        как он записан в системе, например «WH02-PCK-01-01-01-01»).
        """;

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "cell_code": {
              "type": "string",
              "description": "Код ячейки склада точно как он записан в системе"
            }
          },
          "required": ["cell_code"]
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var code = doc.RootElement.TryGetProperty("cell_code", out var c) ? c.GetString() : null;
        if (string.IsNullOrWhiteSpace(code))
        {
            return """{"error":"Параметр 'cell_code' пустой"}""";
        }

        // Ищем ячейку по коду (case-insensitive).
        var allCells = await _cellCatalog.GetAllAsync(cancellationToken: cancellationToken);
        var cell = allCells.FirstOrDefault(x =>
            string.Equals(x.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));

        if (cell is null)
        {
            return $$"""{"cell": null, "message": "Ячейка «{{code}}» не найдена в каталоге"}""";
        }

        var contents = await _stockLocations.GetByCellAsync(cell.Id, cancellationToken);
        if (contents.Count == 0)
        {
            return $$"""{"cell": {"code":"{{cell.Code}}", "warehouse":"{{cell.WarehouseName}}"}, "items": [], "message": "Ячейка пуста"}""";
        }

        var result = new
        {
            cell = new
            {
                code = cell.Code,
                warehouse = cell.WarehouseName,
                zone = cell.ZoneName ?? cell.ZoneCode ?? "—",
                capacity = cell.Capacity
            },
            items = contents.Select(loc => new
            {
                code = loc.ItemCode,
                name = loc.ItemName,
                qty = loc.Quantity,
                available = loc.AvailableQuantity
            }).ToArray()
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
