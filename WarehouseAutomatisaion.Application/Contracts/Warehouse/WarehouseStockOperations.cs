namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

public sealed record StockTransferRequest(
    Guid ItemId,
    Guid SourceCellId,
    Guid TargetCellId,
    decimal Quantity,
    string Actor,
    string? RelatedDocument = null,
    string? Comment = null);

public sealed record StockTransferResult(
    bool Succeeded,
    decimal SourceQuantity,
    decimal TargetQuantity,
    string Message);

public sealed record StockWriteOffRequest(
    Guid ItemId,
    Guid SourceCellId,
    decimal Quantity,
    string Actor,
    string Reason,
    string? RelatedDocument = null,
    string? Comment = null);

public sealed record StockWriteOffResult(
    bool Succeeded,
    decimal SourceQuantity,
    string DocumentNumber,
    string Message);

public sealed record CellInventoryLineInput(
    Guid StockLocationId,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    decimal SystemQuantity,
    decimal ActualQuantity,
    decimal ReservedQuantity,
    string ResolutionCode,
    string Reason,
    string? InvestigationCellCode);

public sealed record CellInventoryCommitRequest(
    Guid CellId,
    string CellCode,
    string WarehouseName,
    string Actor,
    IReadOnlyList<CellInventoryLineInput> Lines,
    string? Comment = null);

public sealed record CellInventoryCommitResult(
    bool Succeeded,
    Guid DocumentId,
    string DocumentNumber,
    int CountedLines,
    int DifferenceLines,
    decimal ShortageQuantity,
    decimal SurplusQuantity,
    string Message);
