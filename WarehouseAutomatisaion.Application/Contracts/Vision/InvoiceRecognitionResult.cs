namespace WarehouseAutomatisaion.Application.Contracts.Vision;

// Sprint 5: результат распознавания накладной AI-сервисом.
// Все поля, кроме Lines и метаданных, nullable — реальные накладные могут
// иметь пропуски (рукописные / частично нечитаемые / нестандартный формат).
public sealed record InvoiceRecognitionResult(
    string? SupplierName,
    string? SupplierTaxId,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? Currency,
    decimal? TotalAmount,
    decimal? TotalVat,
    IReadOnlyList<InvoiceLineItem> Lines,
    string? RawResponseJson,
    double? Confidence,
    string ProviderName,
    DateTimeOffset RecognizedAtUtc,
    TimeSpan Duration);

public sealed record InvoiceLineItem(
    int LineNumber,
    string? Sku,
    string Name,
    string? Unit,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? Vat,
    decimal? Subtotal,
    decimal? Total);
