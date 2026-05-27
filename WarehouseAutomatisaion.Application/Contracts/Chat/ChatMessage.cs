namespace WarehouseAutomatisaion.Application.Contracts.Chat;

// Sprint 12 (AI Chat assistant): контракты для окна «Спроси склад».
// Tool-agnostic — описывают conversation flow без привязки к Anthropic/OpenAI.
// Конкретный SDK мапится в Infrastructure (ClaudeChatService).

public enum ChatRole
{
    User,
    Assistant,
    Tool
}

// Одно сообщение в conversation. Для tool-результатов используем Role.Tool +
// ToolUseId который ассистент ранее запросил.
public sealed record ChatMessage(
    ChatRole Role,
    string Text,
    string? ToolUseId = null,        // только для Tool messages
    IReadOnlyList<ChatToolCall>? ToolCalls = null); // assistant запросы инструментов

// Запрос ассистента вызвать инструмент. Возникает в ответе модели.
public sealed record ChatToolCall(
    string ToolUseId,
    string ToolName,
    string ArgumentsJson);

// Финальный ответ диалога (после всех tool round-trips).
public sealed record ChatResponse(
    string Text,
    IReadOnlyList<ChatMessage> AppendedHistory, // что добавилось (assistant + tool messages)
    string ProviderName,
    int ToolCallsCount,
    TimeSpan Duration);
