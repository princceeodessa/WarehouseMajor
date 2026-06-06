using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

public interface IWmsReadinessReader
{
    Task<WmsReadinessSnapshot> ReadAsync(CancellationToken cancellationToken = default);
}
