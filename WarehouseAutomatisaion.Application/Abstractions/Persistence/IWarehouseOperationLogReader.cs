using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

public interface IWarehouseOperationLogReader
{
    Task<IReadOnlyList<WarehouseOperationLogRecord>> GetRecentAsync(
        int limit,
        string? search,
        CancellationToken cancellationToken = default);
}
