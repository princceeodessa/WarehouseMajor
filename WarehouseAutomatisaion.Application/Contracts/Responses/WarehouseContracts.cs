namespace WarehouseAutomatisaion.Application.Contracts.Responses;

public sealed record OperationResult<T>(bool Success, string? ErrorCode, string? ErrorMessage, T? Value)
{
    public static OperationResult<T> Ok(T value) => new(true, null, null, value);

    public static OperationResult<T> Fail(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage, default);
}

public sealed record WarehouseOverviewResponse(
    int WarehouseCount,
    int CellCount,
    int InventoryLines,
    decimal AverageCellLoadPercent,
    int ActiveTaskCount,
    int CriticalTaskCount,
    IReadOnlyCollection<StockAlertResponse> StockAlerts,
    IReadOnlyCollection<CellLoadResponse> CongestedCells);

public sealed record StockAlertResponse(
    Guid ProductId,
    string Sku,
    string Name,
    decimal AvailableQuantity,
    decimal ReorderPoint,
    decimal DeficitQuantity);

public sealed record CellLoadResponse(
    Guid CellId,
    string WarehouseCode,
    string Address,
    int Capacity,
    int Occupancy,
    decimal LoadPercent);

public sealed record WarehouseTaskResponse(
    Guid Id,
    string Type,
    string Status,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal Quantity,
    int Priority,
    Guid? SourceCellId,
    string? SourceCellAddress,
    Guid? TargetCellId,
    string? TargetCellAddress,
    DateTimeOffset CreatedAtUtc);

public sealed record OneCSyncStatusResponse(
    string SourceSystem,
    bool Healthy,
    bool Simulated,
    DateTimeOffset? LastSuccessfulSyncUtc,
    DateTimeOffset CheckedAtUtc,
    int ImportedProducts,
    int ImportedBalances,
    int ImportedOrders,
    string LastBatchId,
    string LastMessage);
