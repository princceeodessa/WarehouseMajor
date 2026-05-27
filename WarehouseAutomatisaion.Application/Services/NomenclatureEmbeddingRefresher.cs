using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 11 (AI embedding search): пересчёт embeddings для каталога номенклатуры.
// Стратегии:
//   • RefreshMissingAsync — догнать только те items у которых нет embedding для
//     текущего провайдера. Дёшево, инкрементально.
//   • RebuildAllAsync — перегнать ВСЕ items (например при смене модели или
//     при подозрении на повреждение). Дорого по токенам.
//
// Прогресс пробрасывается через IProgress<RefreshProgress> чтобы UI мог обновлять
// status bar в реальном времени без поллинга.
public sealed class NomenclatureEmbeddingRefresher
{
    private readonly IEmbeddingService _embeddingService;
    private readonly INomenclatureCatalogReader _catalogReader;
    private readonly INomenclatureEmbeddingStore _store;

    public NomenclatureEmbeddingRefresher(
        IEmbeddingService embeddingService,
        INomenclatureCatalogReader catalogReader,
        INomenclatureEmbeddingStore store)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _catalogReader = catalogReader ?? throw new ArgumentNullException(nameof(catalogReader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<RefreshResult> RefreshMissingAsync(
        IProgress<RefreshProgress>? progress = null,
        int batchSize = 200,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogReader.GetAllAsync(cancellationToken);
        if (catalog.Count == 0)
        {
            return new RefreshResult(_embeddingService.ProviderName, 0, 0, 0, "Каталог номенклатуры пуст.");
        }

        var missingIds = (await _store.GetMissingAsync(_embeddingService.ProviderName, cancellationToken)).ToHashSet();
        var pending = catalog.Where(r => Guid.TryParse(r.Id, out var g) && missingIds.Contains(g)).ToList();

        return await ProcessBatchesAsync(pending, batchSize, progress, cancellationToken);
    }

    public async Task<RefreshResult> RebuildAllAsync(
        IProgress<RefreshProgress>? progress = null,
        int batchSize = 200,
        CancellationToken cancellationToken = default)
    {
        var catalog = await _catalogReader.GetAllAsync(cancellationToken);
        return await ProcessBatchesAsync(catalog.ToList(), batchSize, progress, cancellationToken);
    }

    private async Task<RefreshResult> ProcessBatchesAsync(
        List<NomenclatureRef> pending,
        int batchSize,
        IProgress<RefreshProgress>? progress,
        CancellationToken cancellationToken)
    {
        var provider = _embeddingService.ProviderName;
        var dimensions = _embeddingService.Dimensions;
        var processed = 0;
        var failed = 0;
        var total = pending.Count;

        progress?.Report(new RefreshProgress(provider, 0, total, "Старт"));

        for (var offset = 0; offset < pending.Count; offset += batchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var batch = pending.Skip(offset).Take(batchSize).ToList();
            var texts = batch.Select(BuildSourceText).ToList();

            try
            {
                var vectors = await _embeddingService.EmbedBatchAsync(texts, cancellationToken);
                if (vectors.Count != batch.Count)
                {
                    failed += batch.Count;
                    continue;
                }

                var upserts = new List<EmbeddingUpsert>(batch.Count);
                for (var i = 0; i < batch.Count; i++)
                {
                    if (!Guid.TryParse(batch[i].Id, out var itemId))
                    {
                        failed++;
                        continue;
                    }
                    upserts.Add(new EmbeddingUpsert(
                        ItemId: itemId,
                        Provider: provider,
                        Dimensions: dimensions,
                        SourceText: texts[i],
                        Embedding: vectors[i]));
                }

                if (upserts.Count > 0)
                {
                    await _store.UpsertManyAsync(upserts, cancellationToken);
                    processed += upserts.Count;
                }

                progress?.Report(new RefreshProgress(
                    provider,
                    Math.Min(processed + failed, total),
                    total,
                    $"Сохранено {processed} / {total}"));
            }
            catch (Exception ex)
            {
                failed += batch.Count;
                progress?.Report(new RefreshProgress(
                    provider,
                    Math.Min(processed + failed, total),
                    total,
                    $"Ошибка batch на offset {offset}: {ex.Message}"));
            }
        }

        return new RefreshResult(
            Provider: provider,
            Processed: processed,
            Failed: failed,
            Total: total,
            Message: failed == 0
                ? $"Готово. Embeddings обновлены: {processed} / {total}."
                : $"С ошибками. Обновлено {processed}, не удалось {failed} (из {total})");
    }

    // Embedding делается по конкатенации Code + Name — это даёт matcher'у больше контекста
    // (например артикул помогает различить «гайка М6» vs «гайка М8»).
    private static string BuildSourceText(NomenclatureRef item)
    {
        var code = (item.Code ?? string.Empty).Trim();
        var name = (item.Name ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            return name;
        }
        if (string.IsNullOrEmpty(name))
        {
            return code;
        }
        return $"{code} {name}";
    }
}

public sealed record RefreshProgress(string Provider, int Done, int Total, string Message);

public sealed record RefreshResult(
    string Provider,
    int Processed,
    int Failed,
    int Total,
    string Message);
