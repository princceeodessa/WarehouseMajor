namespace WarehouseAutomatisaion.Application.Contracts.Receiving;

// Sprint 8 (AI loop closure): summary AI-распознанного черновика приёмки
// для отображения в списке. Детальные строки грузятся отдельным запросом.
public sealed record ReceiptDraftSummary(
    Guid Id,
    string Number,
    string DocumentType,
    DateTime DocumentDate,
    string StatusText,
    string SupplierName,
    string? SupplierTaxId,
    string? InvoiceNumber,
    decimal? TotalAmount,
    int LinesCount,
    decimal TotalQuantity,
    string SourceLabel,
    DateTime CreatedAtUtc);

// Детализация черновика — строки + receiving метаданные.
public sealed record ReceiptDraftDetail(
    ReceiptDraftSummary Header,
    IReadOnlyList<ReceiptDraftLineDetail> Lines);

public sealed record ReceiptDraftLineDetail(
    Guid Id,
    int LineNumber,
    Guid? MatchedItemId,
    string OriginalItemName,
    string? OriginalSku,
    string? Unit,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? Subtotal,
    decimal? Total);
