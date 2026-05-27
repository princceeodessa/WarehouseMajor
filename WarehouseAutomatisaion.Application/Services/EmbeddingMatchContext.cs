namespace WarehouseAutomatisaion.Application.Services;

// Sprint 11 (AI embedding search): опциональный контекст для семантического матчинга
// в InvoiceLineMatcher. Передаётся как параметр Match() — если null, matcher работает
// как раньше (только Levenshtein/partial).
//
// CatalogEmbeddings — все векторы каталога загруженные одним SELECT'ом из
// app_nomenclature_embeddings. Ключ = nomenclature_items.id.
//
// LineEmbeddings — векторы для каждой recognized line (по индексу). Считаются на runtime
// одним batch-вызовом IEmbeddingService.EmbedBatchAsync перед Match.
public sealed record EmbeddingMatchContext(
    IReadOnlyDictionary<Guid, float[]> CatalogEmbeddings,
    IReadOnlyList<float[]> LineEmbeddings);
