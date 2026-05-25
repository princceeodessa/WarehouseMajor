namespace WarehouseAutomatisaion.Application.Contracts.Receiving;

// Sprint 5: черновик приёмочного документа создаваемый AI / оператором.
// Сохраняется в app_warehouse_documents с document_kind='receipt'.
// В Sprint 2 (после публикации OData) outbox будет пушить это в 1С как
// «Поступление товаров и услуг».
public sealed record ReceiptDraft(
    string SupplierName,
    string? SupplierTaxId,
    string? InvoiceNumber,
    DateOnly? InvoiceDate,
    string? Currency,
    decimal? TotalAmount,
    decimal? TotalVat,
    IReadOnlyList<ReceiptDraftLine> Lines,
    string SourceLabel,
    string CreatedByActor,
    string? CommentText = null);

public sealed record ReceiptDraftLine(
    int LineNumber,
    Guid? MatchedItemId,
    string OriginalItemName,
    string? OriginalSku,
    string? Unit,
    decimal Quantity,
    decimal? UnitPrice,
    decimal? Vat,
    decimal? Subtotal,
    decimal? Total);
