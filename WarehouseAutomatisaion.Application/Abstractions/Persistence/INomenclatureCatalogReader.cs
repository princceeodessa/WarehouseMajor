using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 5: чтение каталога номенклатуры для AI line matcher.
// Возвращает только то что нужно матчеру (id/code/name) — не полный Product DTO,
// потому что matcher работает с десятками тысяч строк и нам важна память.
//
// В отличие от существующего IProductRepository (in-memory заглушка),
// этот reader читает реальные nomenclature_items из MySQL.
public interface INomenclatureCatalogReader
{
    Task<IReadOnlyList<NomenclatureRef>> GetAllAsync(CancellationToken cancellationToken = default);
}
