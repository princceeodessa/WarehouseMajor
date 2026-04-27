namespace WarehouseAutomatisaion.Domain.Enums;

public enum WarehouseTaskType
{
    PutAway = 1,
    Picking = 2,
    Replenishment = 3,
    Relocation = 4,
    InventoryCount = 5
}

public enum WarehouseTaskStatus
{
    Planned = 1,
    InProgress = 2,
    Completed = 3,
    Blocked = 4
}
