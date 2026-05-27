using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Точечный поиск ячейки по сканировке: code / qr_payload.
// Отличается от IStorageCellCatalog.GetAllAsync — там полный список для UI,
// а здесь один результат для TSD-скана. Не возвращает «весь склад» в память.
//
// Реализация — Infrastructure/Persistence/MySqlStorageCellLookup.
public interface IStorageCellLookup
{
    Task<StorageCellLookupMatch?> FindAsync(string scanValue, CancellationToken cancellationToken);
}
