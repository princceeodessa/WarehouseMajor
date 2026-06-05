using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

public interface IWarehouseStockOperationService
{
    Task<StockTransferResult> TransferAsync(
        StockTransferRequest request,
        CancellationToken cancellationToken = default);

    Task<StockWriteOffResult> WriteOffAsync(
        StockWriteOffRequest request,
        CancellationToken cancellationToken = default);

    Task<CellInventoryCommitResult> CommitCellInventoryAsync(
        CellInventoryCommitRequest request,
        CancellationToken cancellationToken = default);
}
