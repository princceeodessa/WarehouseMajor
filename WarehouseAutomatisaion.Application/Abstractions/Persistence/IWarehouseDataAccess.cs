using WarehouseAutomatisaion.Domain.Entities;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

public interface IProductRepository
{
    Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken);
    Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken);
}

public interface IStorageCellRepository
{
    Task<IReadOnlyCollection<StorageCell>> GetAllAsync(CancellationToken cancellationToken);
    Task<StorageCell?> GetByIdAsync(Guid cellId, CancellationToken cancellationToken);
}

public interface IInventoryBalanceRepository
{
    Task<IReadOnlyCollection<InventoryBalance>> GetAllAsync(CancellationToken cancellationToken);
}

public interface IWarehouseTaskRepository
{
    Task<IReadOnlyCollection<WarehouseTask>> GetActiveAsync(CancellationToken cancellationToken);
    Task<WarehouseTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken);
    Task AddAsync(WarehouseTask task, CancellationToken cancellationToken);
}

public interface IIntegrationCheckpointRepository
{
    Task<IntegrationCheckpoint> GetAsync(CancellationToken cancellationToken);
    Task SaveAsync(IntegrationCheckpoint checkpoint, CancellationToken cancellationToken);
}
