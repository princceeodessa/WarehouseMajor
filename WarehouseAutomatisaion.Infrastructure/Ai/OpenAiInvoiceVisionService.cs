using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 5: реализация IInvoiceVisionService через OpenAI GPT-4o.
// Structured output: ResponseFormat с JSON schema (strict mode). Это гарантирует
// что AI вернёт валидный JSON по нашей схеме — не нужен fallback parsing.
public sealed class OpenAiInvoiceVisionService : IInvoiceVisionService
{
    private const string JsonSchemaFormatName = "invoice_recognition";

    private static readonly string SystemPrompt = """
        Ты — точный распознаватель российских товарных накладных (ТОРГ-12, УПД, рукописные накладные, торговые документы).

        Извлеки данные из изображения накладной и верни строго по JSON-схеме.

        Правила:
        - Если поле не видно, нечитаемо или отсутствует — верни null. НЕ угадывай и не выдумывай.
        - supplier_name — полное название поставщика как в шапке документа.
        - supplier_tax_id — ИНН поставщика (10 или 12 цифр).
        - invoice_number — номер документа (например «УТ-00001234», «00012345», «145/2026»).
        - invoice_date — дата накладной в формате ISO 8601 YYYY-MM-DD.
        - currency — RUB по умолчанию для российских накладных. USD/EUR если явно указано.
        - total_amount — итоговая сумма с НДС.
        - total_vat — общая сумма НДС.
        - lines — все строки таблицы товаров по порядку, включая последнюю.
        - line_number — порядковый номер строки в таблице (1, 2, 3, ...).
        - sku — артикул/код товара если виден (например «НФ-00001746», «12345»). Иначе null.
        - name — название товара полностью, без сокращений и обрезаний.
        - unit — единица измерения («шт», «кг», «м», «уп», «компл»).
        - quantity — количество (десятичные через точку: 5.000, 12.5).
        - unit_price — цена за единицу без НДС.
        - vat — сумма НДС по строке.
        - subtotal — сумма по строке без НДС (quantity × unit_price).
        - total — итоговая сумма по строке с НДС.

        Если в накладной есть итоговая строка («Итого», «Всего к оплате») — она НЕ должна попадать
        в lines, только данные о товарах.
        """;

    private static readonly string JsonSchema = """
        {
          "type": "object",
          "properties": {
            "supplier_name":   { "type": ["string", "null"] },
            "supplier_tax_id": { "type": ["string", "null"] },
            "invoice_number":  { "type": ["string", "null"] },
            "invoice_date":    { "type": ["string", "null"], "description": "ISO 8601 date YYYY-MM-DD" },
            "currency":        { "type": ["string", "null"] },
            "total_amount":    { "type": ["number", "null"] },
            "total_vat":       { "type": ["number", "null"] },
            "lines": {
              "type": "array",
              "items": {
                "type": "object",
                "properties": {
                  "line_number": { "type": "integer" },
                  "sku":         { "type": ["string", "null"] },
                  "name":        { "type": "string" },
                  "unit":        { "type": ["string", "null"] },
                  "quantity":    { "type": "number" },
                  "unit_price":  { "type": ["number", "null"] },
                  "vat":         { "type": ["number", "null"] },
                  "subtotal":    { "type": ["number", "null"] },
                  "total":       { "type": ["number", "null"] }
                },
                "required": ["line_number", "sku", "name", "unit", "quantity", "unit_price", "vat", "subtotal", "total"],
                "additionalProperties": false
              }
            }
          },
          "required": ["supplier_name", "supplier_tax_id", "invoice_number", "invoice_date", "currency", "total_amount", "total_vat", "lines"],
          "additionalProperties": false
        }
        """;

    private readonly IOptionsMonitor<AiProvidersOptions> _options;
    private readonly ILogger<OpenAiInvoiceVisionService> _logger;

    public OpenAiInvoiceVisionService(
        IOptionsMonitor<AiProvidersOptions> options,
        ILogger<OpenAiInvoiceVisionService> logger)
    {
        _options = options;
        _logger = logger;
    }

    public string ProviderName => $"OpenAI:{_options.CurrentValue.OpenAi.Model}";

    public async Task<InvoiceRecognitionResult> RecognizeAsync(
        InvoiceImagePayload payload,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue.OpenAi;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "OpenAI API key not configured. Set AiProviders:OpenAi:ApiKey in appsettings.local.json.");
        }

        if (!payload.IsSupported())
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.InvalidImage,
                $"Unsupported content type '{payload.ContentType}'. Allowed: {string.Join(", ", InvoiceImagePayload.SupportedContentTypes)}.");
        }

        var stopwatch = Stopwatch.StartNew();
        var client = new ChatClient(model: opts.Model, apiKey: opts.ApiKey);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(
                ChatMessageContentPart.CreateTextPart("Распознай эту накладную."),
                ChatMessageContentPart.CreateImagePart(
                    BinaryData.FromBytes(payload.ImageBytes),
                    payload.ContentType))
        };

        var completionOptions = new ChatCompletionOptions
        {
            MaxOutputTokenCount = opts.MaxTokens,
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: JsonSchemaFormatName,
                jsonSchema: BinaryData.FromString(JsonSchema),
                jsonSchemaIsStrict: true)
        };

        try
        {
            var completion = await client.CompleteChatAsync(messages, completionOptions, cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();

            var rawJson = completion.Value.Content[0].Text;
            _logger.LogInformation(
                "OpenAI vision: {Duration} ms, input tokens: {InputTokens}, output tokens: {OutputTokens}, file: {FileName}",
                stopwatch.ElapsedMilliseconds,
                completion.Value.Usage?.InputTokenCount ?? 0,
                completion.Value.Usage?.OutputTokenCount ?? 0,
                payload.SourceFileName ?? "(in-memory)");

            return ParseResponse(rawJson, stopwatch.Elapsed);
        }
        catch (ClientResultException exception) when (exception.Status == 401)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "OpenAI authentication failed. Check API key.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.RateLimited,
                "OpenAI rate limit exceeded. Retry later.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status == 402)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.QuotaExceeded,
                "OpenAI billing/quota issue.",
                exception);
        }
        catch (ClientResultException exception) when (exception.Status >= 500)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.ProviderUnavailable,
                $"OpenAI server error ({exception.Status}).",
                exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Network error calling OpenAI API.",
                exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "OpenAI request timed out.");
        }
        catch (InvoiceVisionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.Other,
                $"Unexpected error from OpenAI: {exception.Message}",
                exception);
        }
    }

    private InvoiceRecognitionResult ParseResponse(string rawJson, TimeSpan duration)
    {
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
                $"Failed to parse OpenAI response as JSON: {exception.Message}",
                exception);
        }
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
            if (prop.TryGetDecimal(out var dec))
            {
                return dec;
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
        {
            if (prop.TryGetInt32(out var i))
            {
                return i;
            }
        }

        return null;
    }
}
