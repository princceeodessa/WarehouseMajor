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

// Sprint 5: реализация IInvoiceVisionService через Anthropic Claude (vision).
// По умолчанию claude-opus-4-7 — лучшая модель с high-res vision (до 2576px по длинной стороне)
// и adaptive thinking. Prompt caching на статичном system-промпте экономит токены на повторных вызовах.
public sealed class ClaudeInvoiceVisionService : IInvoiceVisionService
{
    private static readonly string SystemPrompt = """
        Ты — точный распознаватель российских товарных накладных (ТОРГ-12, УПД, рукописные накладные, торговые документы).

        Извлеки данные из изображения накладной и верни строго в JSON формате по этой схеме (никакого текста до или после JSON):

        {
          "supplier_name": "string или null",
          "supplier_tax_id": "string или null (ИНН 10 или 12 цифр)",
          "invoice_number": "string или null",
          "invoice_date": "ISO 8601 YYYY-MM-DD или null",
          "currency": "string (RUB по умолчанию) или null",
          "total_amount": "число или null",
          "total_vat": "число или null",
          "lines": [
            {
              "line_number": 1,
              "sku": "string или null",
              "name": "string (полное название товара)",
              "unit": "string или null (шт/кг/м)",
              "quantity": 0.0,
              "unit_price": 0.0,
              "vat": 0.0,
              "subtotal": 0.0,
              "total": 0.0
            }
          ]
        }

        Правила:
        - Если поле не видно или нечитаемо — null. НЕ угадывай.
        - supplier_name — полное название поставщика из шапки документа.
        - invoice_number — номер документа (УТ-00001234, 00012345, 145/2026 и т.д.).
        - invoice_date — формат YYYY-MM-DD.
        - currency — RUB по умолчанию для российских накладных.
        - lines — все строки таблицы товаров по порядку, БЕЗ строки «Итого» в конце.
        - line_number — порядковый номер строки в таблице.
        - quantity и цены — десятичные числа (точка как разделитель).
        - name — полностью, без сокращений.

        Ответь только валидным JSON, начинающимся с { и заканчивающимся }.
        """;

    private readonly IOptionsMonitor<AiProvidersOptions> _options;
    private readonly ILogger<ClaudeInvoiceVisionService> _logger;

    public ClaudeInvoiceVisionService(
        IOptionsMonitor<AiProvidersOptions> options,
        ILogger<ClaudeInvoiceVisionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => $"Anthropic:{_options.CurrentValue.Anthropic.Model}";

    public async Task<InvoiceRecognitionResult> RecognizeAsync(
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
                $"Unsupported content type '{payload.ContentType}'. Allowed: {string.Join(", ", InvoiceImagePayload.SupportedContentTypes)}.");
        }

        var stopwatch = Stopwatch.StartNew();
        AnthropicClient client = new() { ApiKey = opts.ApiKey };

        // System prompt с prompt caching (статичный, экономит токены при повторных вызовах).
        var systemBlocks = new List<TextBlockParam>
        {
            new()
            {
                Text = SystemPrompt,
                CacheControl = opts.EnableCaching ? new CacheControlEphemeral() : null,
            }
        };

        // User message: изображение + краткая инструкция.
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
            new TextBlockParam { Text = "Распознай эту накладную и верни JSON." }
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

            // Извлекаем текстовый контент (Content — union, фильтруем по TextBlock).
            var textBlock = response.Content
                .Select(b => b.Value)
                .OfType<TextBlock>()
                .FirstOrDefault();

            if (textBlock is null || string.IsNullOrWhiteSpace(textBlock.Text))
            {
                throw new InvoiceVisionException(
                    InvoiceVisionFailureKind.MalformedResponse,
                    "Empty text response from Claude.");
            }

            _logger.LogInformation(
                "Claude vision: {Duration} ms, input tokens: {InputTokens}, output tokens: {OutputTokens}, cache_read: {CacheRead}, file: {FileName}",
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
                "Anthropic authentication failed. Check API key.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.RateLimited,
                "Anthropic rate limit exceeded. Retry later.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status == 402)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.QuotaExceeded,
                "Anthropic billing/quota issue.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status >= 500)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.ProviderUnavailable,
                $"Anthropic server error ({exception.Status}).",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Network error calling Anthropic API.",
                exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Anthropic request timed out.");
        }
        catch (InvoiceVisionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.Other,
                $"Unexpected error from Anthropic: {exception.Message}",
                exception);
        }
    }

    private InvoiceRecognitionResult ParseResponse(string rawText, TimeSpan duration)
    {
        // Claude может вернуть JSON в markdown-блоке ```json ... ```. Извлекаем.
        var rawJson = ExtractJsonPayload(rawText);

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            DateOnly? parsedDate = null;
            if (TryGetString(root, "invoice_date", out var dateStr)
                && !string.IsNullOrWhiteSpace(dateStr)
                && DateOnly.TryParse(dateStr, out var date))
            {
                parsedDate = date;
            }

            var lines = new List<InvoiceLineItem>();
            if (root.TryGetProperty("lines", out var linesEl) && linesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var lineEl in linesEl.EnumerateArray())
                {
                    lines.Add(new InvoiceLineItem(
                        LineNumber: TryGetInt(lineEl, "line_number") ?? lines.Count + 1,
                        Sku: TryGetString(lineEl, "sku", out var sku) ? sku : null,
                        Name: (TryGetString(lineEl, "name", out var name) ? name : null) ?? "(без названия)",
                        Unit: TryGetString(lineEl, "unit", out var unit) ? unit : null,
                        Quantity: TryGetDecimal(lineEl, "quantity") ?? 0m,
                        UnitPrice: TryGetDecimal(lineEl, "unit_price"),
                        Vat: TryGetDecimal(lineEl, "vat"),
                        Subtotal: TryGetDecimal(lineEl, "subtotal"),
                        Total: TryGetDecimal(lineEl, "total")));
                }
            }

            return new InvoiceRecognitionResult(
                SupplierName: TryGetString(root, "supplier_name", out var sn) ? sn : null,
                SupplierTaxId: TryGetString(root, "supplier_tax_id", out var tax) ? tax : null,
                InvoiceNumber: TryGetString(root, "invoice_number", out var num) ? num : null,
                InvoiceDate: parsedDate,
                Currency: TryGetString(root, "currency", out var cur) ? cur : null,
                TotalAmount: TryGetDecimal(root, "total_amount"),
                TotalVat: TryGetDecimal(root, "total_vat"),
                Lines: lines,
                RawResponseJson: rawJson,
                Confidence: null,
                ProviderName: ProviderName,
                RecognizedAtUtc: DateTimeOffset.UtcNow,
                Duration: duration);
        }
        catch (JsonException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.MalformedResponse,
                $"Failed to parse Claude response as JSON: {exception.Message}",
                exception);
        }
    }

    // Claude иногда оборачивает JSON в markdown ```json ... ``` блок. Извлекаем.
    private static string ExtractJsonPayload(string raw)
    {
        var text = raw.Trim();

        if (text.StartsWith("```"))
        {
            // ```json\n{...}\n``` или ```\n{...}\n```
            var startIdx = text.IndexOf('\n');
            var endIdx = text.LastIndexOf("```", StringComparison.Ordinal);
            if (startIdx > 0 && endIdx > startIdx)
            {
                text = text[(startIdx + 1)..endIdx].Trim();
            }
        }

        // Если всё ещё нет валидного {...}, ищем первый { и последний }
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

    private static Model ResolveModel(string id)
    {
        return id.ToLowerInvariant() switch
        {
            "claude-opus-4-7" => Model.ClaudeOpus4_7,
            "claude-opus-4-6" => Model.ClaudeOpus4_6,
            "claude-sonnet-4-6" => Model.ClaudeSonnet4_6,
            "claude-haiku-4-5" or "claude-haiku-4-5-20251001" => Model.ClaudeHaiku4_5,
            _ => Model.ClaudeOpus4_7
        };
    }

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
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            if (prop.TryGetDecimal(out var dec)) return dec;
        }
        return null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            if (prop.TryGetInt32(out var i)) return i;
        }
        return null;
    }
}
