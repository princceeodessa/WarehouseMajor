using System.Text.Json;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data.ChatTools;

// Sprint 12: tool «где лежит товар».
// Сначала ищем товар через query_stock-like поиск (чтобы получить item_id),
// потом по item_id показываем все ячейки где он лежит.
public sealed class FindItemLocationTool : IChatTool
{
    private readonly DesktopMySqlBackplaneService _backplane;
    private readonly IStockLocationRepository _stockLocations;

    public FindItemLocationTool(
        DesktopMySqlBackplaneService backplane,
        IStockLocationRepository stockLocations)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
    }

    public string Name => "find_item_location";

    public string Description => """
        Найти в каких ячейках лежит конкретный товар. Вызывай когда оператор спрашивает
        «где лежит X?», «в какой ячейке Y?». На вход — поисковая фраза по названию/коду.
        Возвращает первый матч + список ячеек с qty в каждой.
        """;

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "search": {
              "type": "string",
              "description": "Поисковая фраза по названию или коду товара"
            }
          },
          "required": ["search"]
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var search = doc.RootElement.TryGetProperty("search", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(search))
        {
            return """{"error":"Параметр 'search' пустой"}""";
        }

        // 1. Найдём первый товар по поиску — нам нужен его item_id.
        var found = _backplane.LoadStockBalances(
            warehouseNodeId: null, itemSearch: search, onlyPositive: false, limit: 1);

        if (found.Count == 0)
        {
            return $$"""{"locations": [], "message": "Товар «{{search}}» не найден в каталоге"}""";
        }

        var first = found[0];
        if (!Guid.TryParse(first.ItemId, out var itemId))
        {
            return $$"""{"error":"Item ID '{{first.ItemId}}' не Guid"}""";
        }

        // 2. По item_id — все ячейки.
        var locations = await _stockLocations.GetByItemAsync(itemId, cancellationToken);
        if (locations.Count == 0)
        {
            return $$"""{"item": {"code":"{{first.ItemCode}}", "name":"{{first.ItemName}}"}, "locations": [], "message": "Товар «{{first.ItemName}}» не размещён ни в одной ячейке"}""";
        }

        var result = new
        {
            item = new { code = first.ItemCode, name = first.ItemName, total_quantity = first.Quantity },
            locations = locations.Select(loc => new
            {
                cell = loc.StorageCellCode,
                warehouse = loc.WarehouseName,
                qty = loc.Quantity,
                reserved = loc.ReservedQuantity,
                available = loc.AvailableQuantity
            }).ToArray()
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
