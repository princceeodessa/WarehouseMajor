namespace WarehouseAutomatisaion.Application.Contracts.Vision;

// Sprint 10 (AI photo inventory): результат распознавания фото складской полки
// для инвентаризации. Отличается от InvoiceRecognitionResult: нет поставщика,
// цен, налогов — только список физически наблюдаемых товаров с количеством.
public sealed record ShelfInventoryResult(
    IReadOnlyList<ShelfItem> Items,
    string RawResponseJson,
    string ProviderName,
    DateTimeOffset RecognizedAtUtc,
    TimeSpan Duration);

public sealed record ShelfItem(
    int LineNumber,
    string Name,         // как видно на упаковке / ярлыке (для матчинга)
    string? Sku,         // если есть штрих-код / артикул на упаковке
    string? Unit,        // шт / кг / м / л
    decimal Quantity,    // подсчитанное физически количество
    double? Confidence); // 0..1, опционально — насколько Claude уверен
