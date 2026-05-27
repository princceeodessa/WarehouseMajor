namespace WarehouseAutomatisaion.Application.Abstractions.Persistence;

// Sprint 11: хранилище векторных embeddings для nomenclature_items.
// Конкретный provider хранится вместе с записью, чтобы можно было параллельно
// держать embeddings от разных моделей и плавно мигрировать.
public interface INomenclatureEmbeddingStore
{
    /// <summary>Загрузить все embeddings для указанного провайдера в память.
    /// 8893 items × 1536 float32 = ~54 МБ — нормально для RAM.</summary>
    Task<IReadOnlyDictionary<Guid, float[]>> LoadAllAsync(
        string provider,
        CancellationToken cancellationToken = default);

    /// <summary>Bulk-upsert набора embeddings. Используется batch-recalculator'ом.</summary>
    Task UpsertManyAsync(
        IReadOnlyList<EmbeddingUpsert> embeddings,
        CancellationToken cancellationToken = default);

    /// <summary>Сколько embeddings хранится для провайдера. Для UI прогресса.</summary>
    Task<int> CountAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>ID товаров у которых ещё НЕТ embedding для этого провайдера.
    /// Помогает refresher'у инкрементально догонять каталог.</summary>
    Task<IReadOnlyList<Guid>> GetMissingAsync(
        string provider,
        CancellationToken cancellationToken = default);
}

public sealed record EmbeddingUpsert(
    Guid ItemId,
    string Provider,
    int Dimensions,
    string SourceText,
    float[] Embedding);
