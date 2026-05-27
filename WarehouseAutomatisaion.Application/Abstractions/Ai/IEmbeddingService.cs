namespace WarehouseAutomatisaion.Application.Abstractions.Ai;

// Sprint 11 (AI embedding search): генератор векторных embeddings для текстов.
// Используется в:
//   - NomenclatureEmbeddingRefresher (bulk-расчёт по каталогу)
//   - InvoiceLineMatcher (single embed для распознанной строки на runtime)
//
// Провайдеры: OpenAI text-embedding-3-small (1536 dim, дёшево).
// В будущем — text-embedding-3-large (3072 dim) для лучшей точности
// или local sentence-transformers если уйдём от внешних API.
public interface IEmbeddingService
{
    /// <summary>Сгенерировать embedding для одного текста.</summary>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>Сгенерировать embeddings для нескольких текстов одним запросом
    /// (provider-specific batching, обычно до 2048 inputs). Порядок результата
    /// соответствует порядку входов.</summary>
    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);

    /// <summary>Имя провайдера для хранения в БД, "openai:text-embedding-3-small".</summary>
    string ProviderName { get; }

    /// <summary>Размерность вектора. Используется для валидации при чтении из БД.</summary>
    int Dimensions { get; }
}
