using System.Text.Json;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Services;

namespace WarehouseAutomatisaion.Desktop.Data.ChatTools;

// Sprint 12: tool «куда положить товар» — reuse Sprint 9 CellRecommendationService.
// Сначала ищем item по фразе → потом recommendations по item_id.
public sealed class SuggestCellTool : IChatTool
{
    private readonly DesktopMySqlBackplaneService _backplane;
    private readonly CellRecommendationService _recommender;

    public SuggestCellTool(DesktopMySqlBackplaneService backplane, CellRecommendationService recommender)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
        _recommender = recommender ?? throw new ArgumentNullException(nameof(recommender));
    }

    public string Name => "suggest_cell_for_item";

    public string Description => """
        Подсказать в какую ячейку положить товар на основе истории размещения.
        Вызывай когда оператор спрашивает «куда положить X?», «где разместить Y?».
        Возвращает топ-3 рекомендации с обоснованием каждой (например «уже лежит 50 ед.»).
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

        var found = _backplane.LoadStockBalances(
            warehouseNodeId: null, itemSearch: search, onlyPositive: false, limit: 1);
        if (found.Count == 0)
        {
            return $$"""{"recommendations": [], "message": "Товар «{{search}}» не найден в каталоге"}""";
        }

        var first = found[0];
        if (!Guid.TryParse(first.ItemId, out var itemId))
        {
            return $$"""{"error":"Item ID не Guid"}""";
        }

        var recs = await _recommender.GetTopRecommendationsAsync(itemId, top: 3, cancellationToken);
        if (recs.Count == 0)
        {
            return $$"""{"item": {"code":"{{first.ItemCode}}", "name":"{{first.ItemName}}"}, "recommendations": [], "message": "Нет подходящих ячеек — создайте их в разделе «Ячейки»"}""";
        }

        var result = new
        {
            item = new { code = first.ItemCode, name = first.ItemName },
            recommendations = recs.Select(r => new
            {
                cell = r.Cell.Code,
                warehouse = r.Cell.WarehouseName,
                score = Math.Round(r.Score, 2),
                kind = r.Kind.ToString(),
                reason = r.Reason
            }).ToArray()
        };

        return JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }
}
