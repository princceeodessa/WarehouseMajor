namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Sprint 3: ячейка склада — атомарное место хранения товара.
// Иерархия адреса: WarehouseName / ZoneCode / Row / Rack / Shelf / Cell.
// Human-readable code строится по шаблону "Z1-R3-S2-C5" — для печати и поиска.
//
// Денормализация: WarehouseName хранится строкой (рядом с WarehouseNodeId) для
// быстрого UI без JOIN. В реальном проде warehouse_nodes имеет ID, ячейка
// ссылается через name (legacy convention) + опционально через id.
public sealed record StorageCell(
    Guid Id,
    string Code,
    string? WarehouseNodeId,
    string WarehouseName,
    string? ZoneCode,
    string? ZoneName,
    int RowNo,
    int RackNo,
    int ShelfNo,
    int CellNo,
    string? CellType,
    decimal Capacity,
    string? StatusText,
    string? QrPayload,
    string? CommentText,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

// Запрос на создание/обновление — без id и audit-полей (генерируются в writer).
public sealed record StorageCellRequest(
    string Code,
    string? WarehouseNodeId,
    string WarehouseName,
    string? ZoneCode,
    string? ZoneName,
    int RowNo,
    int RackNo,
    int ShelfNo,
    int CellNo,
    string? CellType,
    decimal Capacity,
    string? StatusText,
    string? CommentText);

// Типы ячеек (свободный текст в БД, enum здесь для подсказок в UI).
public static class CellTypes
{
    public const string Storage = "storage";
    public const string Receiving = "receiving";
    public const string Shipping = "shipping";
    public const string Quarantine = "quarantine";
    public const string Defective = "defective";
    public const string Production = "production";

    public static readonly IReadOnlyList<string> All =
        new[] { Storage, Receiving, Shipping, Quarantine, Defective, Production };
}

public static class CellStatuses
{
    public const string Active = "active";
    public const string Reserved = "reserved";
    public const string Blocked = "blocked";
    public const string Maintenance = "maintenance";

    public static readonly IReadOnlyList<string> All =
        new[] { Active, Reserved, Blocked, Maintenance };
}
