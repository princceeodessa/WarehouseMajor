using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai.Tools;

// Sprint 14: инструмент «сезонность продаж».
// Зовёт /v1/analytics/seasonality и отдаёт модели сводку: сезонные/ровные позиции,
// пик и слабый месяц, рекомендации по закупке к сезону.
public sealed class SeasonalityTool : IChatTool
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OneCSyncAnalyticsClient _client;
    private readonly OneCSyncAssistantOptions _options;

    public SeasonalityTool(OneCSyncAnalyticsClient client, OneCSyncAssistantOptions options)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string Name => "seasonality";

    public string Description => """
        Сезонность продаж по данным 1С: какие товары сезонные, когда пик и слабый месяц,
        в сезоне сейчас позиция или нет, и рекомендации к закупке под сезон. Вызывай при
        вопросах «что сезонное», «когда пик продаж», «когда лучше закупать», «сезон/несезон»,
        «по месяцам», «летом/зимой». Если помесячные продажи ещё не загружены, вернётся
        monthly_available=false — тогда честно скажи, что сезон/несезон пока не считается.
        """;

    public string InputSchemaJson => """
        {
          "type": "object",
          "properties": {
            "limit": {
              "type": "integer",
              "description": "Сколько позиций показать (1-30). По умолчанию из настроек."
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
            ["limit"] = limit.ToString(CultureInfo.InvariantCulture),
            ["price_type_contains"] = _options.PriceTypeContains
        };

        string body;
        try
        {
            body = await _client.GetJsonAsync(_options.SeasonalityPath, query, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            return $$"""{"error":"Не удалось получить сезонность: {{Escape(ex.Message)}}"}""";
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body);
        }
        catch (JsonException)
        {
            return """{"error":"Сезонность вернула ответ неожиданного формата."}""";
        }

        var items = (root?["items"] as JsonArray) ?? new JsonArray();
        var outItems = new JsonArray();
        foreach (var item in items.Take(Math.Min(limit, 12)))
        {
            if (item is null)
            {
                continue;
            }

            outItems.Add(new JsonObject
            {
                ["product_code"] = CloneOrNull(item["product_code"]),
                ["product_name"] = CloneOrNull(item["product_name"]),
                ["seasonality_kind"] = CloneOrNull(item["seasonality_kind"]),
                ["season_state"] = CloneOrNull(item["season_state"]),
                ["peak_month"] = CloneOrNull(item["peak_month"]),
                ["low_month"] = CloneOrNull(item["low_month"]),
                ["stock_qty"] = CloneOrNull(item["stock_qty"]),
                ["recommendation"] = CloneOrNull(item["recommendation"])
            });
        }

        var risks = (root?["risks"] as JsonArray) ?? new JsonArray();
        var outRisks = new JsonArray();
        foreach (var risk in risks.Take(3))
        {
            outRisks.Add(CloneOrNull(risk));
        }

        var result = new JsonObject
        {
            ["monthly_available"] = CloneOrNull(root?["monthly_available"]) ?? false,
            ["summary"] = CloneOrNull(root?["summary"]),
            ["risks"] = outRisks,
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
                return Math.Clamp(limit, 1, 30);
            }
        }
        catch (JsonException)
        {
            // Игнорируем — берём значение из настроек.
        }

        return Math.Clamp(Math.Max(_options.Limit, 15), 1, 30);
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
