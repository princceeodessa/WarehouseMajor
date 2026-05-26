namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Фаза A: позиция товара в конкретной ячейке.
// Дополняет агрегатные остатки (StockBalance) детализацией по storage cells.
public sealed record StockLocation(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid? WarehouseNodeId,
    string WarehouseName,
    Guid StorageCellId,
    string StorageCellCode,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity,
    DateTime LastMovementAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

// Параметры записи позиции. Применяется как UPSERT по (item_id, storage_cell_id).
// При повторном вызове quantity/reserved заменяются (не суммируются) —
// арифметику делает caller (приёмка добавляет, перемещение вычитает).
public sealed record StockLocationUpsert(
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid? WarehouseNodeId,
    string WarehouseName,
    Guid StorageCellId,
    string StorageCellCode,
    decimal Quantity,
    decimal ReservedQuantity = 0m);
