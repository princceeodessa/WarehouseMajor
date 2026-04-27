using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Domain.Entities;
using WarehouseAutomatisaion.Domain.Enums;

namespace WarehouseAutomatisaion.Infrastructure.Persistence;

public sealed class InMemoryWarehouseDataStore
{
    private readonly object _syncRoot = new();
    private readonly List<Product> _products;
    private readonly List<StorageCell> _cells;
    private readonly List<InventoryBalance> _balances;
    private readonly List<WarehouseTask> _tasks;
    private IntegrationCheckpoint _checkpoint;

    public InMemoryWarehouseDataStore()
    {
        _products =
        [
            new Product(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                "PALLET-1200",
                "Euro pallet 1200x800",
                120,
                25),
            new Product(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                "BOX-600",
                "Plastic crate 600x400",
                75,
                20),
            new Product(
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                "FILM-500",
                "Stretch film 500 mm",
                240,
                18)
        ];

        _cells =
        [
            new StorageCell(
                Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAA1"),
                "MAIN",
                "A",
                "A-01-01",
                120,
                102),
            new StorageCell(
                Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAA2"),
                "MAIN",
                "A",
                "A-01-02",
                120,
                115),
            new StorageCell(
                Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBB1"),
                "MAIN",
                "B",
                "B-02-01",
                80,
                42),
            new StorageCell(
                Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCC1"),
                "FAST",
                "P",
                "P-01-01",
                40,
                38)
        ];

        _balances =
        [
            new InventoryBalance(
                Guid.Parse("D1111111-1111-1111-1111-111111111111"),
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAA1"),
                84,
                20,
                DateTimeOffset.UtcNow.AddHours(-6)),
            new InventoryBalance(
                Guid.Parse("D2222222-2222-2222-2222-222222222222"),
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCC1"),
                12,
                10,
                DateTimeOffset.UtcNow.AddHours(-2)),
            new InventoryBalance(
                Guid.Parse("D3333333-3333-3333-3333-333333333333"),
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAA2"),
                40,
                8,
                DateTimeOffset.UtcNow.AddHours(-10)),
            new InventoryBalance(
                Guid.Parse("D4444444-4444-4444-4444-444444444444"),
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBB1"),
                9,
                1,
                DateTimeOffset.UtcNow.AddMinutes(-50))
        ];

        _tasks =
        [
            new WarehouseTask(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                WarehouseTaskType.Replenishment,
                WarehouseTaskStatus.Planned,
                Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Guid.Parse("BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBB1"),
                Guid.Parse("CCCCCCCC-CCCC-CCCC-CCCC-CCCCCCCCCCC1"),
                6,
                9,
                DateTimeOffset.UtcNow.AddMinutes(-25)),
            new WarehouseTask(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                WarehouseTaskType.InventoryCount,
                WarehouseTaskStatus.InProgress,
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Guid.Parse("AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAA2"),
                null,
                1,
                6,
                DateTimeOffset.UtcNow.AddMinutes(-90))
        ];

        _checkpoint = new IntegrationCheckpoint(
            "1C",
            null,
            false,
            true,
            0,
            0,
            0,
            "not-started",
            "Synchronization has not run yet.");
    }

    public IReadOnlyCollection<Product> SnapshotProducts()
    {
        lock (_syncRoot)
        {
            return _products.ToArray();
        }
    }

    public Product? FindProduct(Guid productId)
    {
        lock (_syncRoot)
        {
            return _products.FirstOrDefault(product => product.Id == productId);
        }
    }

    public IReadOnlyCollection<StorageCell> SnapshotCells()
    {
        lock (_syncRoot)
        {
            return _cells.ToArray();
        }
    }

    public StorageCell? FindCell(Guid cellId)
    {
        lock (_syncRoot)
        {
            return _cells.FirstOrDefault(cell => cell.Id == cellId);
        }
    }

    public IReadOnlyCollection<InventoryBalance> SnapshotBalances()
    {
        lock (_syncRoot)
        {
            return _balances.ToArray();
        }
    }

    public IReadOnlyCollection<WarehouseTask> SnapshotActiveTasks()
    {
        lock (_syncRoot)
        {
            return _tasks
                .Where(task => task.Status is WarehouseTaskStatus.Planned or WarehouseTaskStatus.InProgress or WarehouseTaskStatus.Blocked)
                .ToArray();
        }
    }

    public WarehouseTask? FindTask(Guid taskId)
    {
        lock (_syncRoot)
        {
            return _tasks.FirstOrDefault(task => task.Id == taskId);
        }
    }

    public void AddTask(WarehouseTask task)
    {
        lock (_syncRoot)
        {
            _tasks.Add(task);
        }
    }

    public IntegrationCheckpoint GetCheckpoint()
    {
        lock (_syncRoot)
        {
            return _checkpoint;
        }
    }

    public void SaveCheckpoint(IntegrationCheckpoint checkpoint)
    {
        lock (_syncRoot)
        {
            _checkpoint = checkpoint;
        }
    }
}

public sealed class InMemoryProductRepository(InMemoryWarehouseDataStore dataStore) : IProductRepository
{
    public Task<IReadOnlyCollection<Product>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.SnapshotProducts());

    public Task<Product?> GetByIdAsync(Guid productId, CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.FindProduct(productId));
}

public sealed class InMemoryStorageCellRepository(InMemoryWarehouseDataStore dataStore) : IStorageCellRepository
{
    public Task<IReadOnlyCollection<StorageCell>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.SnapshotCells());

    public Task<StorageCell?> GetByIdAsync(Guid cellId, CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.FindCell(cellId));
}

public sealed class InMemoryInventoryBalanceRepository(InMemoryWarehouseDataStore dataStore) : IInventoryBalanceRepository
{
    public Task<IReadOnlyCollection<InventoryBalance>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.SnapshotBalances());
}

public sealed class InMemoryWarehouseTaskRepository(InMemoryWarehouseDataStore dataStore) : IWarehouseTaskRepository
{
    public Task<IReadOnlyCollection<WarehouseTask>> GetActiveAsync(CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.SnapshotActiveTasks());

    public Task<WarehouseTask?> GetByIdAsync(Guid taskId, CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.FindTask(taskId));

    public Task AddAsync(WarehouseTask task, CancellationToken cancellationToken)
    {
        dataStore.AddTask(task);
        return Task.CompletedTask;
    }
}

public sealed class InMemoryIntegrationCheckpointRepository(InMemoryWarehouseDataStore dataStore) : IIntegrationCheckpointRepository
{
    public Task<IntegrationCheckpoint> GetAsync(CancellationToken cancellationToken) =>
        Task.FromResult(dataStore.GetCheckpoint());

    public Task SaveAsync(IntegrationCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        dataStore.SaveCheckpoint(checkpoint);
        return Task.CompletedTask;
    }
}
