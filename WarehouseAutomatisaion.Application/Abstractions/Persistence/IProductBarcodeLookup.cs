using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Точечный поиск товара по сканировке: барcode / SKU / qr_payload.
// Используется и TSD (RegisterScanAsync), и потенциально UI «найти товар».
// Кандидаты нормализуются (UPPER + удаление пробелов/дефисов), поэтому
// scanValue не обязательно совпадает символ-в-символ с базой.
//
// Реализация — Infrastructure/Persistence/MySqlProductBarcodeLookup
// (читает app_product_barcodes + app_catalog_items).
public interface IProductBarcodeLookup
{
    Task<ProductBarcodeMatch?> FindAsync(string scanValue, CancellationToken cancellationToken);
}
