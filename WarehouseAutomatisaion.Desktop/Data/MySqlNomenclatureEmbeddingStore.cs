using WarehouseAutomatisaion.Application.Abstractions.Persistence;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 11: async wrapper над sync persistence в backplane.
public sealed class MySqlNomenclatureEmbeddingStore : INomenclatureEmbeddingStore
{
    private readonly DesktopMySqlBackplaneService _backplane;

    public MySqlNomenclatureEmbeddingStore(DesktopMySqlBackplaneService backplane)
    {
        _backplane = backplane ?? throw new ArgumentNullException(nameof(backplane));
    }

    public Task<IReadOnlyDictionary<Guid, float[]>> LoadAllAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var loaded = _backplane.LoadEmbeddings(provider);
        return Task.FromResult(loaded);
    }

    public Task UpsertManyAsync(
        IReadOnlyList<EmbeddingUpsert> embeddings,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _backplane.BulkUpsertEmbeddings(embeddings);
        return Task.CompletedTask;
    }

    public Task<int> CountAsync(string provider, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.CountEmbeddings(provider));
    }

    public Task<IReadOnlyList<Guid>> GetMissingAsync(
        string provider,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_backplane.LoadMissingEmbeddingItemIds(provider));
    }
}
