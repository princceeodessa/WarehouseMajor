using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Responses;

namespace WarehouseAutomatisaion.Application.Services;

public interface IWarehouseOverviewService
{
    Task<WarehouseOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken);
}

public sealed class WarehouseOverviewService(
    IProductRepository productRepository,
    IStorageCellRepository storageCellRepository,
    IInventoryBalanceRepository inventoryBalanceRepository,
    IWarehouseTaskRepository warehouseTaskRepository,
    ILogger<WarehouseOverviewService> logger) : IWarehouseOverviewService
{
    public async Task<WarehouseOverviewResponse> GetOverviewAsync(CancellationToken cancellationToken)
    {
        var products = await productRepository.GetAllAsync(cancellationToken);
        var cells = await storageCellRepository.GetAllAsync(cancellationToken);
        var balances = await inventoryBalanceRepository.GetAllAsync(cancellationToken);
        var activeTasks = await warehouseTaskRepository.GetActiveAsync(cancellationToken);

        var stockAlerts = products
            .Select(product =>
            {
                var availableQuantity = balances
                    .Where(balance => balance.ProductId == product.Id)
                    .Sum(balance => balance.AvailableQuantity);

                return new StockAlertResponse(
                    product.Id,
                    product.Sku,
                    product.Name,
                    availableQuantity,
                    product.ReorderPoint,
                    Math.Max(product.ReorderPoint - availableQuantity, 0));
            })
            .Where(alert => alert.AvailableQuantity <= alert.ReorderPoint)
            .OrderByDescending(alert => alert.DeficitQuantity)
            .Take(5)
            .ToArray();

        var congestedCells = cells
            .OrderByDescending(cell => cell.LoadPercent)
            .Take(5)
            .Select(cell => new CellLoadResponse(
                cell.Id,
                cell.WarehouseCode,
                cell.Address,
                cell.Capacity,
                cell.Occupancy,
                cell.LoadPercent))
            .ToArray();

        var warehouseCount = cells
            .Select(cell => cell.WarehouseCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var overview = new WarehouseOverviewResponse(
            warehouseCount,
            cells.Count,
            balances.Count,
            cells.Count == 0 ? 0 : Math.Round(cells.Average(cell => cell.LoadPercent), 2),
            activeTasks.Count,
            activeTasks.Count(task => task.Priority >= 8),
            stockAlerts,
            congestedCells);

        logger.LogInformation(
            "Prepared warehouse overview for {WarehouseCount} warehouses with {ActiveTaskCount} active tasks.",
            overview.WarehouseCount,
            overview.ActiveTaskCount);

        return overview;
    }
}
