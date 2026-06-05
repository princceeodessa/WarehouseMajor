using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Chat;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 14: настоящий агентный чат поверх локальной модели (Ollama qwen2.5) на сервере.
//
// Поток: системный промпт + история + сообщение -> сервер /v1/assistant/chat
// (тонкий прокси к Ollama /api/chat с tools) -> модель сама решает, какой
// складской инструмент вызвать -> Major выполняет его локально (IChatTool:
// MySQL-остатки/ячейки или analytics API) -> результат уходит модели обратно
// -> модель отвечает словами.
//
// Режим только чтение: write-инструментов здесь нет.
public sealed class OneCSyncAssistantChatService : IChatService
{
    private const string SystemPrompt = """
        Ты — складской помощник WMS «Major». Помогаешь кладовщику быстро находить
        информацию по складу: остатки товаров, в какой ячейке что лежит, что лежит
        в конкретной ячейке и куда положить новый товар. Также можешь дать аналитику
        по запасам (залежавшийся/медленный/дефицит/лидеры) и сезонности продаж.

        Правила:
        - Любые факты о товарах, остатках, ячейках и аналитике бери ТОЛЬКО из
          инструментов. Никогда не выдумывай числа, коды и названия. Нет данных от
          инструмента — честно скажи, что не нашёл, и попроси уточнить название/код.
        - Если для ответа нужны данные склада — вызови подходящий инструмент. Можно
          вызывать инструменты несколько раз подряд, пока не соберёшь ответ.
        - Отвечай кратко, по-деловому и на русском. Числа называй простыми словами.
        - Ты работаешь в режиме «только чтение»: не меняешь остатки и не проводишь
          операции. Если просят что-то изменить (принять, переместить, списать) —
          объясни, что это делается в разделе «Работа склада»
          (приёмка / перемещение / инвентаризация), и подскажи нужные данные.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly OneCSyncAssistantOptions _options;
    private readonly IReadOnlyList<IChatTool> _tools;
    private readonly OneCSyncTokenResolver _tokenResolver;
    private readonly ILogger<OneCSyncAssistantChatService> _logger;
    private readonly HttpClient _http;

    public OneCSyncAssistantChatService(
        OneCSyncAssistantOptions options,
        IEnumerable<IChatTool> tools,
        OneCSyncTokenResolver tokenResolver,
        ILogger<OneCSyncAssistantChatService> logger,
        HttpClient? http = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tools = tools?.ToArray() ?? Array.Empty<IChatTool>();
        _tokenResolver = tokenResolver ?? throw new ArgumentNullException(nameof(tokenResolver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _http = http ?? new HttpClient();
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 15, 600));
    }

    public string ProviderName =>
        $"1C/Ollama:{(string.IsNullOrWhiteSpace(_options.Model) ? "qwen2.5" : _options.Model)}";

    public async Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userMessage);

        var stopwatch = Stopwatch.StartNew();

        var token = await _tokenResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException(
                "Не найден X-Sync-Token для 1C/Ollama API. Укажите OneCSyncAssistant:SyncToken в appsettings.local.json или положите SSH-ключ work\\.ssh рядом с проектом синхронизации.");
        }

        var messages = BuildInitialMessages(history, userMessage);
        var tools = BuildToolsArray();

        var appended = new List<ChatMessage>();
        var toolCallsCount = 0;
        var finalText = string.Empty;
        var maxIterations = Math.Clamp(_options.MaxToolIterations, 1, 12);

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            var responseNode = await CallChatStepAsync(messages, tools, token, cancellationToken)
                .ConfigureAwait(false);

            var message = responseNode["message"];
            var content = message?["content"]?.GetValue<string>() ?? string.Empty;
            var toolCalls = message?["tool_calls"] as JsonArray;

            if (toolCalls is not { Count: > 0 })
            {
                finalText = content.Trim();
                break;
            }

            // Возвращаем ассистентское сообщение с его tool_calls обратно в диалог.
            messages.Add(CloneNode(message!));

            var displayCalls = new List<ChatToolCall>();
            foreach (var call in toolCalls)
            {
                var function = call?["function"];
                var name = function?["name"]?.GetValue<string>() ?? string.Empty;
                var argumentsJson = ExtractArgumentsJson(function?["arguments"]);
                var callId = call?["id"]?.GetValue<string>() ?? $"call_{toolCallsCount}";

                var result = await ExecuteToolAsync(name, argumentsJson, cancellationToken)
                    .ConfigureAwait(false);

                messages.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_name"] = name,
                    ["content"] = result
                });

                displayCalls.Add(new ChatToolCall(callId, name, argumentsJson));
                toolCallsCount++;
            }

            if (displayCalls.Count > 0)
            {
                appended.Add(new ChatMessage(ChatRole.Assistant, string.Empty, ToolCalls: displayCalls));
            }

            // Если модель попутно дала текст — запомним как запасной финал.
            if (!string.IsNullOrWhiteSpace(content))
            {
                finalText = content.Trim();
            }
        }

        stopwatch.Stop();

        if (string.IsNullOrWhiteSpace(finalText))
        {
            finalText = toolCallsCount > 0
                ? "Я собрал данные инструментами, но не смог сформулировать итог. Попробуй переформулировать вопрос."
                : "Не удалось получить ответ от локальной модели. Попробуй ещё раз.";
        }

        return new ChatResponse(
            Text: finalText,
            AppendedHistory: appended,
            ProviderName: ProviderName,
            ToolCallsCount: toolCallsCount,
            Duration: stopwatch.Elapsed);
    }

    private async Task<string> ExecuteToolAsync(string name, string argumentsJson, CancellationToken cancellationToken)
    {
        var tool = _tools.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (tool is null)
        {
            _logger.LogWarning("Модель запросила неизвестный инструмент {ToolName}", name);
            return $$"""{"error":"Инструмент {{name}} недоступен"}""";
        }

        try
        {
            return await tool.ExecuteAsync(argumentsJson, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Инструмент {ToolName} упал на аргументах {Args}", name, argumentsJson);
            return $$"""{"error":"Инструмент {{name}} завершился ошибкой"}""";
        }
    }

    private async Task<JsonNode> CallChatStepAsync(
        JsonArray messages,
        JsonArray tools,
        string token,
        CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["messages"] = CloneNode(messages)
        };

        if (tools.Count > 0)
        {
            body["tools"] = CloneNode(tools);
        }

        if (!string.IsNullOrWhiteSpace(_options.Model))
        {
            body["model"] = _options.Model;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri(_options.ChatPath));
        request.Headers.TryAddWithoutValidation("X-Sync-Token", token);
        request.Content = new StringContent(body.ToJsonString(JsonOptions), Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new AssistantApiException(response.StatusCode, $"1C/Ollama chat {(int)response.StatusCode}: {responseBody}");
        }

        try
        {
            return JsonNode.Parse(responseBody)
                   ?? throw new AssistantApiException(response.StatusCode, "1C/Ollama chat вернул пустой ответ.");
        }
        catch (JsonException ex)
        {
            throw new AssistantApiException(response.StatusCode, "1C/Ollama chat вернул ответ неожиданного формата.", ex);
        }
    }

    private JsonArray BuildInitialMessages(IReadOnlyList<ChatMessage> history, string userMessage)
    {
        var messages = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = SystemPrompt }
        };

        foreach (var item in history)
        {
            if (string.IsNullOrWhiteSpace(item.Text))
            {
                continue;
            }

            var role = item.Role switch
            {
                ChatRole.User => "user",
                ChatRole.Assistant => "assistant",
                ChatRole.Tool => "tool",
                _ => "user"
            };

            messages.Add(new JsonObject { ["role"] = role, ["content"] = item.Text });
        }

        messages.Add(new JsonObject { ["role"] = "user", ["content"] = userMessage });
        return messages;
    }

    private JsonArray BuildToolsArray()
    {
        var array = new JsonArray();
        foreach (var tool in _tools)
        {
            JsonNode parameters;
            try
            {
                parameters = JsonNode.Parse(tool.InputSchemaJson) ?? new JsonObject();
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Инструмент {ToolName} имеет некорректную JSON-схему, пропускаю", tool.Name);
                continue;
            }

            array.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = parameters
                }
            });
        }

        return array;
    }

    private static string ExtractArgumentsJson(JsonNode? argumentsNode)
    {
        if (argumentsNode is null)
        {
            return "{}";
        }

        // Ollama/qwen отдают arguments объектом; некоторые модели — строкой с JSON.
        if (argumentsNode is JsonValue value && value.TryGetValue<string>(out var asString))
        {
            return string.IsNullOrWhiteSpace(asString) ? "{}" : asString;
        }

        return argumentsNode.ToJsonString(JsonOptions);
    }

    private static JsonNode CloneNode(JsonNode node)
    {
        // Узел не может иметь двух родителей — пересоздаём через сериализацию.
        return JsonNode.Parse(node.ToJsonString(JsonOptions))!;
    }

    private Uri BuildUri(string path)
    {
        var baseUrl = NormalizeBaseUrl(_options.BaseUrl);
        var normalizedPath = "/" + (path ?? string.Empty).TrimStart('/');
        return new Uri(baseUrl + normalizedPath, UriKind.Absolute);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        return string.IsNullOrWhiteSpace(baseUrl)
            ? "https://xn--b1apclaccz4czb.xn--p1ai/onec-sync"
            : baseUrl.Trim().TrimEnd('/');
    }

    private sealed class AssistantApiException : Exception
    {
        public AssistantApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
        }

        public HttpStatusCode StatusCode { get; }
    }
}
