using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5 Task 15: wrapper для INomenclatureCatalogReader.
// Делегирует BackplaneService.LoadNomenclatureRefs() — sync→async обёртка.
// Caching: пока не нужен (9000 строк × ~80 байт = 700 KB, читается за десятки мс).
// Когда станет узким — добавим in-memory cache с TTL.
public sealed class MySqlNomenclatureCatalogReader : INomenclatureCatalogReader
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlNomenclatureCatalogReader(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane;
    }

    public Task<IReadOnlyList<NomenclatureRef>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadNomenclatureRefs());
    }
}
