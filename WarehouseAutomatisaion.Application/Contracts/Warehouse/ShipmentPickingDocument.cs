namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Контракты для TSD-сборки расходных накладных.
// Source: app_sales_documents (kind='shipment') + app_sales_document_lines.
// Прогресс сборки считается по app_warehouse_documents (type='ТСД: Сборка')
// связанным через related_document = sales.number.

public sealed record ShipmentPickingDocumentSummary(
    Guid DocumentId,
    string Number,
    DateTime DocumentDate,
    string CustomerName,
    string WarehouseName,
    string Status,
    int LineCount,
    decimal RequiredQuantity,
    decimal PickedQuantity,
    decimal RemainingQuantity);

public sealed record ShipmentPickingDocumentDetails(
    Guid DocumentId,
    string Number,
    DateTime DocumentDate,
    string CustomerName,
    string WarehouseName,
    string Status,
    int LineCount,
    decimal RequiredQuantity,
    decimal PickedQuantity,
    decimal RemainingQuantity,
    IReadOnlyList<ShipmentPickingDocumentLine> Lines);

public sealed record ShipmentPickingDocumentLine(
    int LineNo,
    string ItemCode,
    string ItemName,
    string UnitName,
    decimal RequiredQuantity,
    decimal PickedQuantity,
    decimal RemainingQuantity,
    string Status);

public sealed record ShipmentPickingCompletionResult(
    bool Completed,
    Guid DocumentId,
    string DocumentNumber,
    string Status,
    decimal RequiredQuantity,
    decimal PickedQuantity,
    decimal RemainingQuantity,
    string Message);
