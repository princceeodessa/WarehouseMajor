using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 10 (AI photo inventory): распознавание фото складской полки/стеллажа
// для инвентаризации. Использует тот же Anthropic SDK что ClaudeInvoiceVisionService,
// но с другим системным промптом — фокус на физическом подсчёте товаров,
// а не на полях накладной.
public sealed class ClaudeShelfInventoryVisionService : IShelfInventoryVisionService
{
    private static readonly string SystemPrompt = """
        Ты — точный инвентаризатор склада. На вход — фото полки, стеллажа, ящика или участка склада.
        Твоя задача — посчитать товары которые ФИЗИЧЕСКИ видны на изображении.

        Верни строго JSON по схеме (без текста до/после):

        {
          "items": [
            {
              "line_number": 1,
              "name": "название товара (как написано на упаковке/ярлыке/штрих-коде, или твоё описание если ярлыка нет)",
              "sku": "артикул или штрих-код если видно на упаковке, иначе null",
              "unit": "шт / кг / л / м / упак / коробка",
              "quantity": 0.0,
              "confidence": 0.0
            }
          ]
        }

        Правила:
        - quantity — целое число штук/упаковок. Если несколько одинаковых — суммируй в одну строку с qty=N.
        - name — максимально информативное (бренд + размер + цвет если различимо).
        - sku — ТОЛЬКО если ясно виден артикул или штрих-код на упаковке. Иначе null.
        - confidence — 0..1, твоя уверенность в правильности этой строки.
        - Если на полке смешаны разные товары — выдай отдельные строки для каждого вида.
        - Если фото размытое или товаров не видно — items: [] (пустой массив).
        - НЕ выдумывай цены, налоги, поставщиков — этого нет на полке.

        Ответь только валидным JSON, начинающимся с { и заканчивающимся }.
        """;

    private readonly IOptionsMonitor<AiProvidersOptions> _options;
    private readonly ILogger<ClaudeShelfInventoryVisionService> _logger;

    public ClaudeShelfInventoryVisionService(
        IOptionsMonitor<AiProvidersOptions> options,
        ILogger<ClaudeShelfInventoryVisionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => $"Anthropic-Shelf:{_options.CurrentValue.Anthropic.Model}";

    public async Task<ShelfInventoryResult> RecognizeShelfAsync(
        InvoiceImagePayload payload,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue.Anthropic;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "Anthropic API key not configured. Set AiProviders:Anthropic:ApiKey in appsettings.local.json.");
        }

        if (!payload.IsSupported())
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.InvalidImage,
                $"Unsupported content type '{payload.ContentType}'.");
        }

        var stopwatch = Stopwatch.StartNew();
        AnthropicClient client = new() { ApiKey = opts.ApiKey };

        var systemBlocks = new List<TextBlockParam>
        {
            new()
            {
                Text = SystemPrompt,
                CacheControl = opts.EnableCaching ? new CacheControlEphemeral() : null,
            }
        };

        var imageBase64 = Convert.ToBase64String(payload.ImageBytes);
        var userContent = new List<ContentBlockParam>
        {
            new ImageBlockParam
            {
                Source = new Base64ImageSource
                {
                    MediaType = payload.ContentType,
                    Data = imageBase64,
                }
            },
            new TextBlockParam { Text = "Посчитай товары на этой полке и верни JSON." }
        };

        var parameters = new MessageCreateParams
        {
            Model = ResolveModel(opts.Model),
            MaxTokens = opts.MaxTokens,
            System = systemBlocks,
            Messages = [new() { Role = Role.User, Content = userContent }],
        };

        try
        {
            var response = await client.Messages.Create(parameters);
            stopwatch.Stop();

            var textBlock = response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .FirstOrDefault();

            if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
            {
                throw new InvoiceVisionException(
                    InvoiceVisionFailureKind.MalformedResponse,
                    "Empty text response from Claude (shelf).");
            }

            _logger.LogInformation(
                "Claude shelf inventory: {Duration} ms, input tokens: {InputTokens}, output tokens: {OutputTokens}, cache_read: {CacheRead}, file: {FileName}",
                stopwatch.ElapsedMilliseconds,
                response.Usage?.InputTokens ?? 0,
                response.Usage?.OutputTokens ?? 0,
                response.Usage?.CacheReadInputTokens ?? 0,
                payload.SourceFileName ?? "(in-memory)");

            return ParseResponse(textBlock.Text, stopwatch.Elapsed);
        }
        catch (ClientResultException exception) when (exception.Status == 401)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "Anthropic authentication failed (shelf). Check API key.", exception);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.RateLimited,
                "Anthropic rate limit exceeded (shelf).", exception);
        }
        catch (ClientResultException exception) when (exception.Status == 402)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.QuotaExceeded,
                "Anthropic billing/quota issue (shelf).", exception);
        }
        catch (ClientResultException exception) when (exception.Status >= 500)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.ProviderUnavailable,
                $"Anthropic server error ({exception.Status}) (shelf).", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Network error calling Anthropic API (shelf).", exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Anthropic request timed out (shelf).");
        }
        catch (InvoiceVisionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.Other,
                $"Unexpected error from Anthropic (shelf): {exception.Message}", exception);
        }
    }

    private ShelfInventoryResult ParseResponse(string rawText, TimeSpan duration)
    {
        var rawJson = ExtractJsonPayload(rawText);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            var items = new List<ShelfItem>();
            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in itemsEl.EnumerateArray())
                {
                    items.Add(new ShelfItem(
                        LineNumber: TryGetInt(el, "line_number") ?? items.Count + 1,
                        Name: (TryGetString(el, "name", out var name) ? name : null) ?? "(не распознан)",
                        Sku: TryGetString(el, "sku", out var sku) ? sku : null,
                        Unit: TryGetString(el, "unit", out var unit) ? unit : null,
                        Quantity: TryGetDecimal(el, "quantity") ?? 0m,
                        Confidence: TryGetDouble(el, "confidence")));
                }
            }

            return new ShelfInventoryResult(
                Items: items,
                RawResponseJson: rawJson,
                ProviderName: ProviderName,
                RecognizedAtUtc: DateTimeOffset.UtcNow,
                Duration: duration);
        }
        catch (JsonException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.MalformedResponse,
                $"Failed to parse Claude shelf response as JSON: {exception.Message}", exception);
        }
    }

    private static string ExtractJsonPayload(string raw)
    {
        var text = raw.Trim();
        if (text.StartsWith("```"))
        {
            var startIdx = text.IndexOf('\n');
            var endIdx = text.LastIndexOf("```", StringComparison.Ordinal);
            if (startIdx > 0 && endIdx > startIdx)
            {
                text = text[(startIdx + 1)..endIdx].Trim();
            }
        }
        if (!text.StartsWith("{"))
        {
            var open = text.IndexOf('{');
            var close = text.LastIndexOf('}');
            if (open >= 0 && close > open)
            {
                text = text[open..(close + 1)];
            }
        }
        return text;
    }

    private static Model ResolveModel(string id) => id.ToLowerInvariant() switch
    {
        "claude-opus-4-7" => Model.ClaudeOpus4_7,
        "claude-opus-4-6" => Model.ClaudeOpus4_6,
        "claude-sonnet-4-6" => Model.ClaudeSonnet4_6,
        "claude-haiku-4-5" or "claude-haiku-4-5-20251001" => Model.ClaudeHaiku4_5,
        _ => Model.ClaudeOpus4_7
    };

    private static bool TryGetString(JsonElement element, string name, out string? value)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString();
            return value is not null;
        }
        value = null;
        return false;
    }

    private static decimal? TryGetDecimal(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDecimal(out var dec))
        {
            return dec;
        }
        return null;
    }

    private static double? TryGetDouble(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDouble(out var d))
        {
            return d;
        }
        return null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt32(out var i))
        {
            return i;
        }
        return null;
    }
}
