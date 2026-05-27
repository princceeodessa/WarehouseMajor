namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Результат точечного поиска ячейки по сканировке (код ячейки / qr_payload).
// Отличается от полного StorageCell — содержит только то что нужно UI/TSD
// после распознавания скана.
public sealed record StorageCellLookupMatch(
    Guid Id,
    string Code,
    string WarehouseName);
