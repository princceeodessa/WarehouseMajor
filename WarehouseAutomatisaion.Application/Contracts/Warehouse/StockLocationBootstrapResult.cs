namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

public sealed record StockLocationBootstrapResult(
    int SourceRows,
    decimal SourceQuantity,
    int CellsCreated,
    int LocationsAffected,
    Guid OperationId);
