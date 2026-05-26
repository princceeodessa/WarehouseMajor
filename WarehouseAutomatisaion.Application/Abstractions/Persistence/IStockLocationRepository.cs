using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Фаза A: чтение + запись позиций товара в ячейках.
// Reader API — для UI («что лежит в ячейке», «где лежит товар»).
// Writer API — для приёмки (Фаза B), перемещения (Фаза C), инвентаризации.
public interface IStockLocationRepository
{
    /// <summary>Все позиции в указанной ячейке.</summary>
    Task<IReadOnlyList<StockLocation>> GetByCellAsync(Guid storageCellId, CancellationToken cancellationToken = default);

    /// <summary>Все ячейки где лежит указанный товар (по всем складам).</summary>
    Task<IReadOnlyList<StockLocation>> GetByItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Все позиции на складе (с фильтром по warehouse_name — denormalized).</summary>
    Task<IReadOnlyList<StockLocation>> GetByWarehouseAsync(string warehouseName, CancellationToken cancellationToken = default);

    /// <summary>UPSERT позиции. quantity/reserved заменяются полностью.
    /// Если позиция новая — INSERT, иначе UPDATE по UNIQUE (item_id, storage_cell_id).</summary>
    Task UpsertAsync(StockLocationUpsert upsert, CancellationToken cancellationToken = default);

    /// <summary>Удалить позицию (после перемещения / списания всего товара из ячейки).</summary>
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
