using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Data;

// Фаза A: wrapper IStockLocationRepository над BackplaneService.
public sealed class MySqlStockLocationRepository : IStockLocationRepository
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlStockLocationRepository(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane;
    }

    public Task<IReadOnlyList<StockLocation>> GetByCellAsync(
        Guid storageCellId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadStockLocationsByCell(storageCellId));
    }

    public Task<IReadOnlyList<StockLocation>> GetByItemAsync(
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadStockLocationsByItem(itemId));
    }

    public Task<IReadOnlyList<StockLocation>> GetByWarehouseAsync(
        string warehouseName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadStockLocationsByWarehouse(warehouseName));
    }

    public Task UpsertAsync(StockLocationUpsert upsert, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.UpsertStockLocation(upsert);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.DeleteStockLocation(id);
        return Task.CompletedTask;
    }
}
