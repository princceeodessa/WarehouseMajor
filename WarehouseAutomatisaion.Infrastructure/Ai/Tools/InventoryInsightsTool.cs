using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai.Tools;

// Sprint 14: инструмент «складская аналитика по запасам».
// Зовёт /v1/analytics/inventory-insights (расчёт по синхронизированным данным 1С)
// и отдаёт модели компактный список залежалых / медленных / дефицитных / лидеров.
public sealed class InventoryInsightsTool : IChatTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OneCSyncAnalyticsClient _client;
    private readonly OneCSyncAssistantOptions _options;

    public InventoryInsightsTool(OneCSyncAnalyticsClient client, OneCSyncAssistantOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "inventory_insights";

    public string Description => """
        Складская аналитика по запасам из данных 1С: залежавшийся товар (dead_stock),
        медленно продающиеся позиции (slow_mover), риск дефицита (stockout_risk) и
        лидеры продаж (sales_leader). Вызывай когда спрашивают «что залежалось»,
        «что плохо продаётся», «где скоро закончится / риск дефицита», «что продвигать»,
        «лидеры продаж», «что закупить». Возвращает счётчики по категориям и позиции
        с остатком, продажами и покрытием в днях.
        """;

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "limit": {
              "type": "integer",
              "description": "Сколько позиций показать на категорию (1-20). По умолчанию из настроек."
            }
          },
          "required": []
        }
        """;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var limit = ResolveLimit(argumentsJson);
        var query = new Dictionary<string, string?>
        {
            ["source_system"] = _options.SourceSystem,
            ["sales_period_days"] = _options.SalesPeriodDays.ToString(CultureInfo.InvariantCulture),
            ["slow_cover_days"] = _options.SlowCoverDays.ToString(CultureInfo.InvariantCulture),
            ["low_cover_days"] = _options.LowCoverDays.ToString(CultureInfo.InvariantCulture),
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["price_type_contains"] = _options.PriceTypeContains
        };

        string body;
        try
        {
            body = await _client.GetJsonAsync(_options.InventoryInsightsPath, query, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return $$"""{"error":"Не удалось получить аналитику по запасам: {{Escape(ex.Message)}}"}""";
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return """{"error":"Аналитика вернула ответ неожиданного формата."}""";
        }

        var items = (root?["items"] as JsonArray) ?? new JsonArray();
        if (items.Count == 0)
        {
            return """{"counts":{},"items":[],"message":"По загруженным данным 1С товарных сигналов не найдено."}""";
        }

        var outItems = new JsonArray();
        foreach (var item in items.Take(Math.Min(limit * 4, 20)))
        {
            if (item is null)
            {
                continue;
            }

            outItems.Add(new JsonObject
            {
                ["category"] = CloneOrNull(item["category"]),
                ["product_code"] = CloneOrNull(item["product_code"]),
                ["product_name"] = CloneOrNull(item["product_name"]),
                ["stock_qty"] = CloneOrNull(item["stock_qty"]),
                ["sales_qty"] = CloneOrNull(item["sales_qty"]),
                ["cover_days"] = CloneOrNull(item["cover_days"]),
                ["reason"] = CloneOrNull(item["reason"])
            });
        }

        var result = new JsonObject
        {
            ["counts"] = CloneOrNull(root?["counts"]),
            ["items"] = outItems
        };

        return result.ToJsonString(JsonOptions);
    }

    private int ResolveLimit(string argumentsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("limit", out var limitElement)
                && limitElement.TryGetInt32(out var limit))
            {
                return Math.Clamp(limit, 1, 20);
            }
        }
        catch (JsonException)
        {
            // Игнорируем — берём значение из настроек.
        }

        return Math.Clamp(_options.Limit, 1, 20);
    }

    private static JsonNode? CloneOrNull(JsonNode? node)
    {
        return node is null ? null : JsonNode.Parse(node.ToJsonString(JsonOptions));
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
