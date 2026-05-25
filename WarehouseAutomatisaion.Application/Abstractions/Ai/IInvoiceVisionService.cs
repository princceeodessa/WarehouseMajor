using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 5: контракт сервиса распознавания накладных.
// Реализации (Sprint 5): OpenAiInvoiceVisionService (GPT-4o vision).
// Будущие реализации: ClaudeInvoiceVisionService (claude-sonnet-4-5 vision).
// Выбор реализации через config AiProviders:Default.
public interface IInvoiceVisionService
{
    /// <summary>
    /// Распознаёт накладную из изображения и возвращает структурированный результат.
    /// </summary>
    /// <param name="payload">Изображение (JPEG/PNG/WebP). PDF в Sprint 5 не поддерживается,
    /// конвертация PDF→images добавится позже.</param>
    /// <exception cref="InvoiceVisionException">При любой ошибке распознавания —
    /// сетевой, rate limit, malformed response, и т.д. См. Kind для категории.</exception>
    Task<InvoiceRecognitionResult> RecognizeAsync(
        InvoiceImagePayload payload,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Имя провайдера в формате "Vendor:Model", например "OpenAI:gpt-4o".
    /// Используется для логирования и аудита.
    /// </summary>
    string ProviderName { get; }
}
