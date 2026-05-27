namespace WarehouseAutomatisaion.Application.Contracts.Warehouse;

// Результат точечного поиска товара по сканировке (баркод / код / qr_payload).
// Используется TSD и UI «найти товар по штрихкоду» — единый контракт.
public sealed record ProductBarcodeMatch(
    Guid Id,
    string Code,
    string Name);
