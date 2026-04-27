using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Requests;
using WarehouseAutomatisaion.Application.Contracts.Responses;
using WarehouseAutomatisaion.Domain.Entities;
using WarehouseAutomatisaion.Domain.Enums;

namespace WarehouseAutomatisaion.Application.Services;

public interface IWarehouseTaskService
{
    Task<IReadOnlyCollection<WarehouseTaskResponse>> GetActiveTasksAsync(CancellationToken cancellationToken);
    Task<WarehouseTaskResponse?> GetTaskByIdAsync(Guid taskId, CancellationToken cancellationToken);
    Task<OperationResult<WarehouseTaskResponse>> CreateReplenishmentTaskAsync(
        CreateReplenishmentTaskRequest request,
        CancellationToken cancellationToken);
}

public sealed class WarehouseTaskService(
    IProductRepository productRepository,
    IStorageCellRepository storageCellRepository,
    IInventoryBalanceRepository inventoryBalanceRepository,
    IWarehouseTaskRepository warehouseTaskRepository,
    ILogger<WarehouseTaskService> logger) : IWarehouseTaskService
{
    public async Task<IReadOnlyCollection<WarehouseTaskResponse>> GetActiveTasksAsync(CancellationToken cancellationToken)
    {
        var tasks = await warehouseTaskRepository.GetActiveAsync(cancellationToken);
        return await MapTasksAsync(tasks, cancellationToken);
    }

    public async Task<WarehouseTaskResponse?> GetTaskByIdAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await warehouseTaskRepository.GetByIdAsync(taskId, cancellationToken);
        if (task is null)
        {
            return null;
        }

        var mapped = await MapTasksAsync([task], cancellationToken);
        return mapped.Single();
    }

    public async Task<OperationResult<WarehouseTaskResponse>> CreateReplenishmentTaskAsync(
        CreateReplenishmentTaskRequest request,
        CancellationToken cancellationToken)
    {
        var product = await productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product is null)
        {
            return OperationResult<WarehouseTaskResponse>.Fail(
                "product_not_found",
                $"Product '{request.ProductId}' does not exist.");
        }

        var cells = await storageCellRepository.GetAllAsync(cancellationToken);
        var balances = await inventoryBalanceRepository.GetAllAsync(cancellationToken);

        var sourceBalance = balances
            .Where(balance => balance.ProductId == request.ProductId && balance.AvailableQuantity >= request.Quantity)
            .Where(balance => request.SourceCellId is null || balance.StorageCellId == request.SourceCellId)
            .OrderByDescending(balance => balance.AvailableQuantity)
            .FirstOrDefault();

        if (sourceBalance is null)
        {
            return OperationResult<WarehouseTaskResponse>.Fail(
                "source_not_available",
                "No source cell contains enough available stock for the requested replenishment.");
        }

        var targetCell = request.TargetCellId is not null
            ? cells.FirstOrDefault(cell => cell.Id == request.TargetCellId.Value)
            : cells
                .Where(cell => cell.Id != sourceBalance.StorageCellId)
                .OrderBy(cell => cell.LoadPercent)
                .FirstOrDefault();

        if (targetCell is null)
        {
            return OperationResult<WarehouseTaskResponse>.Fail(
                "target_not_found",
                "No target cell is available for the replenishment task.");
        }

        if (targetCell.Id == sourceBalance.StorageCellId)
        {
            return OperationResult<WarehouseTaskResponse>.Fail(
                "same_cell",
                "The source cell and target cell must be different.");
        }

        var task = new WarehouseTask(
            Guid.NewGuid(),
            WarehouseTaskType.Replenishment,
            WarehouseTaskStatus.Planned,
            product.Id,
            sourceBalance.StorageCellId,
            targetCell.Id,
            request.Quantity,
            request.Priority,
            DateTimeOffset.UtcNow);

        await warehouseTaskRepository.AddAsync(task, cancellationToken);

        logger.LogInformation(
            "Created replenishment task {TaskId} for product {Sku} from {SourceCellId} to {TargetCellId}.",
            task.Id,
            product.Sku,
            task.SourceCellId,
            task.TargetCellId);

        var response = await GetTaskByIdAsync(task.Id, cancellationToken);
        return OperationResult<WarehouseTaskResponse>.Ok(response!);
    }

    private async Task<IReadOnlyCollection<WarehouseTaskResponse>> MapTasksAsync(
        IReadOnlyCollection<WarehouseTask> tasks,
        CancellationToken cancellationToken)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        var cells = await storageCellRepository.GetAllAsync(cancellationToken);

        var productLookup = products.ToDictionary(product => product.Id);
        var cellLookup = cells.ToDictionary(cell => cell.Id);

        return tasks
            .OrderByDescending(task => task.Priority)
            .ThenByDescending(task => task.CreatedAtUtc)
            .Select(task =>
            {
                productLookup.TryGetValue(task.ProductId, out var product);
                var sourceCell = task.SourceCellId is null
                    ? null
                    : cellLookup.GetValueOrDefault(task.SourceCellId.Value);
                var targetCell = task.TargetCellId is null
                    ? null
                    : cellLookup.GetValueOrDefault(task.TargetCellId.Value);

                return new WarehouseTaskResponse(
                    task.Id,
                    task.Type.ToString(),
                    task.Status.ToString(),
                    task.ProductId,
                    product?.Sku ?? "unknown",
                    product?.Name ?? "Unknown product",
                    task.Quantity,
                    task.Priority,
                    task.SourceCellId,
                    sourceCell?.Address,
                    task.TargetCellId,
                    targetCell?.Address,
                    task.CreatedAtUtc);
            })
            .ToArray();
    }
}
