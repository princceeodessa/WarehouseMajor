using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 3 Task 19: wrapper для IStorageCellCatalog.
// Делегирует sync-методам BackplaneService с async-обёрткой.
public sealed class MySqlStorageCellCatalog : IStorageCellCatalog
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlStorageCellCatalog(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane;
    }

    public Task<IReadOnlyList<StorageCell>> GetAllAsync(
        string? warehouseFilter = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadStorageCells(warehouseFilter));
    }

    public Task<StorageCell?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.GetStorageCellById(id));
    }

    public Task<Guid> CreateAsync(StorageCellRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.CreateStorageCell(request));
    }

    public Task UpdateAsync(Guid id, StorageCellRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.UpdateStorageCell(id, request);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.DeleteStorageCell(id);
        return Task.CompletedTask;
    }
}
