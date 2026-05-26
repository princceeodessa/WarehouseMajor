using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 10: контракт сервиса распознавания фото складской полки для инвентаризации.
// Отличается от IInvoiceVisionService: вход и выход разные DTO, разные системные промпты.
public interface IShelfInventoryVisionService
{
    /// <summary>
    /// Распознаёт фото полки/стеллажа и возвращает список наблюдаемых товаров с их qty.
    /// </summary>
    Task<ShelfInventoryResult> RecognizeShelfAsync(
        InvoiceImagePayload payload,
        CancellationToken cancellationToken = default);

    string ProviderName { get; }
}
