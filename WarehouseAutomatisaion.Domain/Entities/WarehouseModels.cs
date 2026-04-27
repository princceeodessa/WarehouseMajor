using WarehouseAutomatisaion.Domain.Enums;

namespace WarehouseAutomatisaion.Domain.Entities;

public sealed record Product(
    Guid Id,
    string Sku,
    string Name,
    decimal TurnoverPerMonth,
    decimal ReorderPoint);

public sealed record StorageCell(
    Guid Id,
    string WarehouseCode,
    string Zone,
    string Address,
    int Capacity,
    int Occupancy)
{
    public decimal LoadPercent => Capacity == 0 ? 0 : Math.Round((decimal)Occupancy / Capacity * 100, 2);
}

public sealed record InventoryBalance(
    Guid Id,
    Guid ProductId,
    Guid StorageCellId,
    decimal Quantity,
    decimal ReservedQuantity,
    DateTimeOffset LastMovementAtUtc)
{
    public decimal AvailableQuantity => Quantity - ReservedQuantity;
}

public sealed record WarehouseTask(
    Guid Id,
    WarehouseTaskType Type,
    WarehouseTaskStatus Status,
    Guid ProductId,
    Guid? SourceCellId,
    Guid? TargetCellId,
    decimal Quantity,
    int Priority,
    DateTimeOffset CreatedAtUtc);

public sealed record IntegrationCheckpoint(
    string ExternalSystem,
    DateTimeOffset? LastSuccessfulSyncUtc,
    bool IsHealthy,
    bool LastRunWasSimulated,
    int ImportedProducts,
    int ImportedBalances,
    int ImportedOrders,
    string LastBatchId,
    string LastMessage);
