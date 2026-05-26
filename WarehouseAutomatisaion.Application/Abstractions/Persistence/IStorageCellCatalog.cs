using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 3: CRUD для master-data ячеек склада.
// Идемпотентность Create — клиент решает (через unique constraint на code).
// Delete — soft вариант через StatusText="archived" предпочтительнее hard,
// потому что ячейки могут быть ссылкой из исторических документов (Sprint 4+).
public interface IStorageCellCatalog
{
    Task<IReadOnlyList<StorageCell>> GetAllAsync(
        string? warehouseFilter = null,
        CancellationToken cancellationToken = default);

    Task<StorageCell?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Guid> CreateAsync(StorageCellRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid id, StorageCellRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
