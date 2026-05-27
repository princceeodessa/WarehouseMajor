using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Chat;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 12 (AI Chat assistant): реализация IChatService через прямой HTTP к Anthropic API.
// Tool use loop полностью внутри — UI отправляет сообщение → возвращается final answer.
//
// Почему не через Anthropic SDK: tool-related types в Stainless-generated пакете
// слишком сложно использовать (union discriminators, ContentBlockParam без public ctor).
// REST API стабилен и документирован → дёшево написать руками через HttpClient.
public sealed class ClaudeChatService : IChatService
{
    private const string ApiBase = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly string SystemPrompt = """
        Ты — помощник склада в WMS Major. Отвечай по-русски, кратко и по делу.
        Тебе доступны инструменты для запроса данных со склада (товары, ячейки, остатки).
        Когда оператор спрашивает «сколько у нас Х», «где лежит Y», «куда положить Z» —
        обязательно используй инструменты ДО ответа, не угадывай. Если данных нет —
        честно скажи что не нашёл.

        Формат ответа:
        - Короткие предложения, без воды.
        - Если есть числовые данные — выводи их как «Х шт» / «Y ячеек».
        - Если оператор спрашивает действие («принять 10 шт») — НЕ делай его сам,
          просто скажи «Это нужно сделать через окно Приёмки» и подскажи как.
        """;

    private readonly IOptionsMonitor<AiProvidersOptions> _options;
    private readonly IReadOnlyList<IChatTool> _tools;
    private readonly ILogger<ClaudeChatService> _logger;
    private readonly HttpClient _http;

    public ClaudeChatService(
        IOptionsMonitor<AiProvidersOptions> options,
        IEnumerable<IChatTool> tools,
        ILogger<ClaudeChatService> logger,
        HttpClient? http = null)
    {
        _options = options;
        _tools = tools.ToList();
        _logger = logger;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public string ProviderName => $"Anthropic-Chat:{_options.CurrentValue.Anthropic.Model}";

    public async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue.Anthropic;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "Anthropic API key not configured. Set AiProviders:Anthropic:ApiKey in appsettings.local.json.");
        }

        var stopwatch = Stopwatch.StartNew();

        // Конвертируем history + новый user message в Anthropic messages array.
        var messages = BuildInitialMessages(history, userMessage);
        var appendedHistory = new List<ChatMessage>(); // что мы добавили в результате round-trips
        var toolCallsCount = 0;
        string finalText = string.Empty;

        // Tool use loop: до 6 итераций безопасности (обычно ≤2).
        const int MaxIterations = 6;
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var requestPayload = new
            {
                model = opts.Model,
                max_tokens = opts.MaxTokens,
                system = SystemPrompt,
                tools = BuildToolsArray(),
                messages = messages
            };

            var response = await CallApiAsync(opts.ApiKey, requestPayload, cancellationToken);

            // Парсим content blocks.
            var contentBlocks = response.RootElement.GetProperty("content");
            var stopReason = response.RootElement.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;

            // Извлекаем текст из текущего ответа ассистента (может содержать рассуждения до tool_use).
            var assistantText = ExtractText(contentBlocks);

            // Извлекаем tool_use вызовы.
            var toolUses = ExtractToolUses(contentBlocks);

            // Сохраняем сырой assistant content для следующего round-trip (нужен Anthropic'у в context).
            var assistantContent = response.RootElement.GetProperty("content").Clone();
            messages.Add(new
            {
                role = "assistant",
                content = JsonElementToObject(assistantContent)
            });
            appendedHistory.Add(new ChatMessage(
                Role: ChatRole.Assistant,
                Text: assistantText,
                ToolCalls: toolUses.Count > 0
                    ? toolUses.Select(t => new ChatToolCall(t.Id, t.Name, t.InputJson)).ToArray()
                    : null));

            if (stopReason == "end_turn" || toolUses.Count == 0)
            {
                // Финальный ответ — выходим из цикла.
                finalText = assistantText;
                break;
            }

            // Выполняем все tool calls и собираем tool_result blocks для следующего request.
            var toolResultBlocks = new List<object>();
            foreach (var call in toolUses)
            {
                toolCallsCount++;
                var tool = _tools.FirstOrDefault(t =>
                    string.Equals(t.Name, call.Name, StringComparison.OrdinalIgnoreCase));

                string resultText;
                bool isError = false;
                if (tool is null)
                {
                    resultText = $"{{\"error\":\"Tool '{call.Name}' is not registered.\"}}";
                    isError = true;
                }
                else
                {
                    try
                    {
                        resultText = await tool.ExecuteAsync(call.InputJson, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        resultText = $"{{\"error\":\"{EscapeJson(ex.Message)}\"}}";
                        isError = true;
                        _logger.LogWarning(ex, "Tool {ToolName} failed", call.Name);
                    }
                }

                toolResultBlocks.Add(new
                {
                    type = "tool_result",
                    tool_use_id = call.Id,
                    content = resultText,
                    is_error = isError
                });

                appendedHistory.Add(new ChatMessage(
                    Role: ChatRole.Tool,
                    Text: resultText,
                    ToolUseId: call.Id));
            }

            // Добавляем user message с tool_result'ами для следующего round-trip.
            messages.Add(new
            {
                role = "user",
                content = toolResultBlocks
            });
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "Chat: {ToolCalls} tool calls, {Duration} ms, provider={Provider}",
            toolCallsCount, stopwatch.ElapsedMilliseconds, ProviderName);

        return new ChatResponse(
            Text: string.IsNullOrWhiteSpace(finalText)
                ? "(Не удалось получить ответ — превышен лимит итераций tool use.)"
                : finalText,
            AppendedHistory: appendedHistory,
            ProviderName: ProviderName,
            ToolCallsCount: toolCallsCount,
            Duration: stopwatch.Elapsed);
    }

    // ========== Helpers ==========

    private async Task<JsonDocument> CallApiAsync(string apiKey, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiBase);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", AnthropicVersion);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => new InvoiceVisionException(
                        InvoiceVisionFailureKind.AuthenticationFailed, "Anthropic 401: invalid API key."),
                    System.Net.HttpStatusCode.TooManyRequests => new InvoiceVisionException(
                        InvoiceVisionFailureKind.RateLimited, "Anthropic 429: rate limit."),
                    System.Net.HttpStatusCode.PaymentRequired => new InvoiceVisionException(
                        InvoiceVisionFailureKind.QuotaExceeded, "Anthropic 402: billing/quota."),
                    _ when (int)response.StatusCode >= 500 => new InvoiceVisionException(
                        InvoiceVisionFailureKind.ProviderUnavailable,
                        $"Anthropic {(int)response.StatusCode}: {body}"),
                    _ => new InvoiceVisionException(
                        InvoiceVisionFailureKind.Other,
                        $"Anthropic {(int)response.StatusCode}: {body}")
                };
            }

            return JsonDocument.Parse(body);
        }
        catch (HttpRequestException ex)
        {
            throw new InvoiceVisionException(InvoiceVisionFailureKind.NetworkError, "HTTP error", ex);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new InvoiceVisionException(InvoiceVisionFailureKind.NetworkError, "Anthropic request timed out.");
        }
    }

    private List<object> BuildInitialMessages(IReadOnlyList<ChatMessage> history, string userMessage)
    {
        // history содержит ТОЛЬКО пары [user → assistant final], без tool round-trips
        // (UI хранит compressed view: user + один final assistant per turn).
        // Tool round-trip context остаётся внутри одного SendAsync вызова.
        var messages = new List<object>();
        foreach (var m in history)
        {
            if (m.Role == ChatRole.User)
            {
                messages.Add(new { role = "user", content = m.Text });
            }
            else if (m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text))
            {
                messages.Add(new { role = "assistant", content = m.Text });
            }
        }
        messages.Add(new { role = "user", content = userMessage });
        return messages;
    }

    private List<object> BuildToolsArray()
    {
        return _tools.Select(t => (object)new
        {
            name = t.Name,
            description = t.Description,
            input_schema = JsonSerializer.Deserialize<object>(t.InputSchemaJson)!
        }).ToList();
    }

    private static string ExtractText(JsonElement content)
    {
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.GetString() == "text"
                && block.TryGetProperty("text", out var text))
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.Append(text.GetString());
            }
        }
        return sb.ToString().Trim();
    }

    private static List<ToolUseRequest> ExtractToolUses(JsonElement content)
    {
        var list = new List<ToolUseRequest>();
        foreach (var block in content.EnumerateArray())
        {
            if (block.TryGetProperty("type", out var type)
                && type.GetString() == "tool_use")
            {
                var id = block.GetProperty("id").GetString() ?? string.Empty;
                var name = block.GetProperty("name").GetString() ?? string.Empty;
                var input = block.TryGetProperty("input", out var inp) ? inp.GetRawText() : "{}";
                list.Add(new ToolUseRequest(id, name, input));
            }
        }
        return list;
    }

    // Конвертирует JsonElement в object для повторной сериализации
    // (нужно чтобы вставить assistant.content обратно в next request как есть).
    private static object JsonElementToObject(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value)),
            JsonValueKind.Array => element.EnumerateArray()
                .Select(JsonElementToObject).ToList(),
            JsonValueKind.String => element.GetString()!,
            JsonValueKind.Number => element.TryGetInt64(out var l) ? (object)l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null!,
            _ => element.GetRawText()
        };
    }

    private static string EscapeJson(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ");

    private sealed record ToolUseRequest(string Id, string Name, string InputJson);
}
