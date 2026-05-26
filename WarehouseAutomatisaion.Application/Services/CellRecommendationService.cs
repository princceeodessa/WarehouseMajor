using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 9 (AI suggest cell): rule-based рекомендатор ячейки для приёмки.
// Никакого ML — простая иерархия правил основанных на истории stock_locations.
//
// Приоритет (top 3):
//  1. Ячейки где этот товар уже лежал (наследственность размещения) — сильнейший сигнал.
//     Score = 1.0 если qty > 0, 0.85 если qty = 0 (товар был, но разошёлся)
//  2. Если в правиле 1 < 3 ячеек — добиваем свободными storage-ячейками
//     на тех же складах. Score = 0.5..0.6
//  3. Если ещё нужно — любые storage-ячейки по списку. Score = 0.4
//
// Performance: используем только TWO запроса к БД:
//   - GetAllAsync() для всех ячеек
//   - GetByItemAsync(itemId) для истории конкретного товара
// Это O(1) round-trips к БД независимо от размера склада.
public sealed class CellRecommendationService
{
    private readonly IStockLocationRepository _stockLocations;
    private readonly IStorageCellCatalog _cellCatalog;

    public CellRecommendationService(
        IStockLocationRepository stockLocations,
        IStorageCellCatalog cellCatalog)
    {
        _stockLocations = stockLocations ?? throw new ArgumentNullException(nameof(stockLocations));
        _cellCatalog = cellCatalog ?? throw new ArgumentNullException(nameof(cellCatalog));
    }

    public async Task<IReadOnlyList<CellRecommendation>> GetTopRecommendationsAsync(
        Guid itemId,
        int top = 3,
        CancellationToken cancellationToken = default)
    {
        if (top <= 0)
        {
            return Array.Empty<CellRecommendation>();
        }

        // 2 запроса — это всё что нужно.
        var allCellsTask = _cellCatalog.GetAllAsync(cancellationToken: cancellationToken);
        var historyTask = _stockLocations.GetByItemAsync(itemId, cancellationToken);
        await Task.WhenAll(allCellsTask, historyTask);

        var allCells = allCellsTask.Result;
        var historyForItem = historyTask.Result;

        if (allCells.Count == 0)
        {
            return Array.Empty<CellRecommendation>();
        }

        var result = new List<CellRecommendation>();
        var usedCellIds = new HashSet<Guid>();
        var historicalWarehouses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Правило 1: история размещения товара
        foreach (var loc in historyForItem.OrderByDescending(l => l.Quantity))
        {
            var cell = allCells.FirstOrDefault(c => c.Id == loc.StorageCellId);
            if (cell is null || !usedCellIds.Add(cell.Id))
            {
                continue;
            }

            historicalWarehouses.Add(cell.WarehouseName);

            var reason = loc.Quantity > 0
                ? $"Уже лежит {loc.Quantity:N3} ед. в этой ячейке"
                : "Этот товар тут раньше хранился (сейчас 0)";

            result.Add(new CellRecommendation(
                Cell: cell,
                Score: loc.Quantity > 0 ? 1.0 : 0.85,
                Reason: reason,
                Kind: RecommendationKind.History));

            if (result.Count >= top)
            {
                return result;
            }
        }

        // Правило 2: storage-ячейки на исторических складах
        if (historicalWarehouses.Count > 0)
        {
            foreach (var cell in allCells
                .Where(c => historicalWarehouses.Contains(c.WarehouseName))
                .Where(c => !usedCellIds.Contains(c.Id))
                .Where(IsStorageCell)
                .OrderBy(c => c.Code))
            {
                if (!usedCellIds.Add(cell.Id))
                {
                    continue;
                }
                result.Add(new CellRecommendation(
                    Cell: cell,
                    Score: 0.55,
                    Reason: $"Свободная storage-ячейка на «{cell.WarehouseName}» (этот товар уже на этом складе)",
                    Kind: RecommendationKind.SameZone));

                if (result.Count >= top)
                {
                    return result;
                }
            }
        }

        // Правило 3: любые storage-ячейки
        foreach (var cell in allCells
            .Where(c => !usedCellIds.Contains(c.Id))
            .Where(IsStorageCell)
            .OrderBy(c => c.WarehouseName)
            .ThenBy(c => c.Code))
        {
            if (!usedCellIds.Add(cell.Id))
            {
                continue;
            }
            result.Add(new CellRecommendation(
                Cell: cell,
                Score: 0.4,
                Reason: $"Storage-ячейка на «{cell.WarehouseName}»",
                Kind: RecommendationKind.FreeStorage));

            if (result.Count >= top)
            {
                return result;
            }
        }

        return result;
    }

    private static bool IsStorageCell(StorageCell cell)
    {
        if (string.IsNullOrEmpty(cell.CellType))
        {
            return true; // тип не задан — считаем storage
        }
        return string.Equals(cell.CellType, CellTypes.Storage, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record CellRecommendation(
    StorageCell Cell,
    double Score,
    string Reason,
    RecommendationKind Kind);

public enum RecommendationKind
{
    History,        // товар уже тут лежал
    SameZone,       // на том же складе
    FreeStorage     // любая storage-ячейка
}
