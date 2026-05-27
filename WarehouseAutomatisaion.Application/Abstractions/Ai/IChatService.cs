using WarehouseAutomatisaion.Application.Contracts.Chat;

namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 12: основная абстракция чата. Принимает историю + новое сообщение,
// возвращает ответ модели после tool-use loop (выполнение всех tool calls внутри).
//
// История conversation хранится в UI (in-memory, не persist). Для нашего сценария
// (один оператор, короткие сессии) этого достаточно — persist можно добавить позже.
public interface IChatService
{
    Task<ChatResponse> SendAsync(
        IReadOnlyList<ChatMessage> history,
        string userMessage,
        CancellationToken cancellationToken = default);

    string ProviderName { get; }
}
