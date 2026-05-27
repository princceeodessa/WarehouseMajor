using System.Text.Json;
using WarehouseAutomatisaion.Application.Abstractions.Ai;

namespace WarehouseAutomatisaion.Desktop.Data.ChatTools;

// Sprint 12: tool «найти товар на складах».
// Использует уже существующий backplane.LoadStockBalances (то же что фильтр в Остатки workspace).
public sealed class QueryStockTool : IChatTool
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public QueryStockTool(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
    }

    public string Name => "query_stock";

    public string Description => """
        Найти товары по поисковой фразе и показать остатки по складам.
        Вызывай когда оператор спрашивает «сколько у нас Х?», «есть ли в наличии Y?»,
        «остатки по Z». Возвращает топ-20 матчей с qty/доступно по каждому складу.
        """;

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "search": {
              "type": "string",
              "description": "Поисковая фраза по названию или коду товара (например «лопата», «НФ-001»)"
            },
            "only_positive": {
              "type": "boolean",
              "description": "Если true — показать только товары с qty > 0. По умолчанию true."
            }
          },
          "required": ["search"]
        }
        """;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        using var doc = JsonDocument.Parse(argumentsJson);
        var root = doc.RootElement;
        var search = root.TryGetProperty("search", out var s) ? s.GetString() : null;
        var onlyPositive = !root.TryGetProperty("only_positive", out var op)
            || op.ValueKind != JsonValueKind.False;

        if (string.IsNullOrWhiteSpace(search))
        {
            return Task.FromResult("""{"error":"Параметр 'search' пустой"}""");
        }

        var rows = _backplane.LoadStockBalances(
            warehouseNodeId: null,
            itemSearch: search,
            onlyPositive: onlyPositive,
            limit: 20);

        if (rows.Count == 0)
        {
            return Task.FromResult($$"""{"matches": [], "message": "Не нашёл товаров с «{{search}}»"}""");
        }

        var result = new
        {
            count = rows.Count,
            matches = rows.Select(r => new
            {
                code = r.ItemCode,
                name = r.ItemName,
                warehouse = r.WarehouseName,
                qty = r.Quantity,
                reserved = r.ReservedQuantity,
                available = r.AvailableQuantity,
            }).ToArray()
        };

        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        return Task.FromResult(json);
    }
}
