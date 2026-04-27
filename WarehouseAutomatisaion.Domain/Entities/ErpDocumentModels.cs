using WarehouseAutomatisaion.Domain.Enums;

namespace WarehouseAutomatisaion.Domain.Entities;

public abstract record ErpDocument(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId);

public sealed record PaymentScheduleLine(
    Guid Id,
    DateOnly DueDate,
    decimal PaymentPercent,
    decimal Amount,
    decimal TaxAmount);

public sealed record CommercialDocumentLine(
    Guid Id,
    Guid ItemId,
    Guid? CharacteristicId,
    Guid? BatchId,
    Guid? UnitOfMeasureId,
    decimal Quantity,
    decimal Price,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal Amount,
    string? VatRateCode,
    decimal TaxAmount,
    decimal Total,
    string? Content);

public sealed record StockMovementLine(
    Guid Id,
    Guid ItemId,
    Guid? CharacteristicId,
    Guid? BatchId,
    Guid? UnitOfMeasureId,
    decimal Quantity,
    Guid? SourceWarehouseNodeId,
    Guid? SourceStorageBinId,
    Guid? TargetWarehouseNodeId,
    Guid? TargetStorageBinId,
    decimal ReservedQuantity,
    decimal CollectedQuantity);

public sealed record InventoryCountLine(
    Guid Id,
    Guid ItemId,
    Guid? CharacteristicId,
    Guid? BatchId,
    Guid? UnitOfMeasureId,
    decimal BookQuantity,
    decimal ActualQuantity,
    decimal DifferenceQuantity);

public sealed record AdditionalChargeLine(
    Guid Id,
    string Name,
    decimal Amount,
    string? AllocationRule);

public sealed record PriceRegistrationLine(
    Guid Id,
    Guid ItemId,
    Guid? CharacteristicId,
    Guid? UnitOfMeasureId,
    Guid PriceTypeId,
    decimal NewPrice,
    decimal? PreviousPrice,
    string CurrencyCode);

public sealed record SalesOrder(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid CustomerId,
    Guid? ContractId,
    Guid? PriceTypeId,
    Guid? WarehouseNodeId,
    Guid? ReserveWarehouseNodeId,
    Guid? StorageBinId,
    LifecycleStatus Status,
    IReadOnlyCollection<CommercialDocumentLine> Lines,
    IReadOnlyCollection<PaymentScheduleLine> PaymentSchedule)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record SalesInvoice(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid CustomerId,
    Guid? ContractId,
    Guid? PriceTypeId,
    Guid? CompanyBankAccountId,
    Guid? CashboxId,
    LifecycleStatus Status,
    decimal TotalAmount,
    IReadOnlyCollection<CommercialDocumentLine> Lines,
    IReadOnlyCollection<PaymentScheduleLine> PaymentSchedule)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record SalesShipment(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid CustomerId,
    Guid? ContractId,
    Guid? SalesOrderId,
    Guid? PriceTypeId,
    Guid? WarehouseNodeId,
    Guid? StorageBinId,
    Guid? CarrierId,
    decimal TotalAmount,
    IReadOnlyCollection<CommercialDocumentLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record PurchaseOrder(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid SupplierId,
    Guid? ContractId,
    Guid? PartnerPriceTypeId,
    Guid? LinkedSalesOrderId,
    Guid? WarehouseNodeId,
    Guid? ReserveWarehouseNodeId,
    LifecycleStatus Status,
    IReadOnlyCollection<CommercialDocumentLine> Lines,
    IReadOnlyCollection<PaymentScheduleLine> PaymentSchedule)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record SupplierInvoice(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid SupplierId,
    Guid? ContractId,
    Guid? PurchaseOrderId,
    Guid? CompanyBankAccountId,
    Guid? SupplierBankAccountId,
    Guid? CashboxId,
    Guid? PartnerPriceTypeId,
    decimal TotalAmount,
    IReadOnlyCollection<CommercialDocumentLine> Lines,
    IReadOnlyCollection<PaymentScheduleLine> PaymentSchedule)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record PurchaseReceipt(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid SupplierId,
    Guid? ContractId,
    Guid? PurchaseOrderId,
    Guid? WarehouseNodeId,
    Guid? StorageBinId,
    Guid? PartnerPriceTypeId,
    decimal TotalAmount,
    IReadOnlyCollection<CommercialDocumentLine> Lines,
    IReadOnlyCollection<AdditionalChargeLine> AdditionalCharges)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record TransferOrder(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    Guid? CustomerOrderId,
    Guid SourceWarehouseNodeId,
    Guid TargetWarehouseNodeId,
    DateOnly RequestedTransferDate,
    LifecycleStatus Status,
    IReadOnlyCollection<StockMovementLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record StockTransfer(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    Guid? TransferOrderId,
    Guid SourceWarehouseNodeId,
    Guid TargetWarehouseNodeId,
    Guid? SourceStorageBinId,
    Guid? TargetStorageBinId,
    IReadOnlyCollection<StockMovementLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record InventoryCount(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    Guid WarehouseNodeId,
    Guid? StorageBinId,
    DateOnly? FinishedOn,
    IReadOnlyCollection<InventoryCountLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record StockReservationDocument(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    Guid? SalesOrderId,
    StockReservationPlace SourcePlace,
    StockReservationPlace TargetPlace,
    IReadOnlyCollection<StockMovementLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record StockWriteOff(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    string CurrencyCode,
    Guid WarehouseNodeId,
    Guid? StorageBinId,
    Guid? InventoryCountId,
    Guid? PriceTypeId,
    string? Reason,
    IReadOnlyCollection<CommercialDocumentLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);

public sealed record PriceRegistrationDocument(
    Guid Id,
    string Number,
    DateTime DocumentDate,
    DocumentPostingState PostingState,
    Guid OrganizationId,
    Guid? AuthorId,
    Guid? ResponsibleEmployeeId,
    string? Comment,
    Guid? BaseDocumentId,
    Guid? ProjectId,
    IReadOnlyCollection<PriceRegistrationLine> Lines)
    : ErpDocument(Id, Number, DocumentDate, PostingState, OrganizationId, AuthorId, ResponsibleEmployeeId, Comment, BaseDocumentId, ProjectId);
