using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

public interface IStockLocationBootstrapper
{
    Task<StockLocationBootstrapResult> BootstrapUnplacedAsync(
        string actorUserName,
        CancellationToken cancellationToken = default);
}
