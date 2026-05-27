using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 5 Task 17: orchestrator связывает четыре кубика
//   IInvoiceVisionService           — распознавание фото
//   InvoiceLineMatcher              — Levenshtein/exact/partial fallback matcher
//   INomenclatureCatalogReader      — загрузка каталога для matcher'а
//   IInvoiceMatchOverrideStore      — learning loop из подтверждений оператора
// Используется UI и smoke-tool. Один публичный метод RecognizeAndMatchAsync.
public sealed class InvoiceRecognitionService
{
    private readonly IInvoiceVisionService _vision;
    private readonly INomenclatureCatalogReader _catalog;
    private readonly InvoiceLineMatcher _matcher;
    private readonly IInvoiceMatchOverrideStore? _overrideStore;
    private readonly IEmbeddingService? _embeddingService;
    private readonly INomenclatureEmbeddingStore? _embeddingStore;
    private readonly ILogger<InvoiceRecognitionService> _logger;

    public InvoiceRecognitionService(
        IInvoiceVisionService vision,
        INomenclatureCatalogReader catalog,
        InvoiceLineMatcher matcher,
        ILogger<InvoiceRecognitionService> logger,
        IInvoiceMatchOverrideStore? overrideStore = null,
        IEmbeddingService? embeddingService = null,
        INomenclatureEmbeddingStore? embeddingStore = null)
    {
        _vision = vision;
        _catalog = catalog;
        _matcher = matcher;
        _overrideStore = overrideStore;
        _embeddingService = embeddingService;
        _embeddingStore = embeddingStore;
        _logger = logger;
    }

    public async Task<InvoiceRecognitionWithMatches> RecognizeAndMatchAsync(
        InvoiceImagePayload payload,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Starting recognition for {FileName} ({ContentType}, {BytesCount} bytes)",
            payload.SourceFileName ?? "(in-memory)",
            payload.ContentType,
            payload.ImageBytes.Length);

        var recognition = await _vision.RecognizeAsync(payload, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Recognized {LinesCount} lines from {Provider} in {Duration} ms. Loading catalog...",
            recognition.Lines.Count,
            recognition.ProviderName,
            recognition.Duration.TotalMilliseconds);

        var catalog = await _catalog.GetAllAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Catalog loaded: {CatalogSize} items. Matching...",
            catalog.Count);

        // Sprint 11: если AI embeddings доступны — собираем контекст для семантического матчинга.
        // Loaded только если есть и сервис, и store, и хотя бы один embedding в БД.
        EmbeddingMatchContext? embeddingContext = null;
        if (_embeddingService is not null && _embeddingStore is not null && recognition.Lines.Count > 0)
        {
            try
            {
                var provider = _embeddingService.ProviderName;
                var catalogEmbeddings = await _embeddingStore.LoadAllAsync(provider, cancellationToken).ConfigureAwait(false);
                if (catalogEmbeddings.Count > 0)
                {
                    var queries = recognition.Lines
                        .Select(l => string.IsNullOrWhiteSpace(l.Sku) ? (l.Name ?? string.Empty) : $"{l.Sku} {l.Name}")
                        .ToList();
                    var lineVectors = await _embeddingService.EmbedBatchAsync(queries, cancellationToken).ConfigureAwait(false);
                    embeddingContext = new EmbeddingMatchContext(catalogEmbeddings, lineVectors);
                    _logger.LogInformation(
                        "Semantic context: {Vectors} catalog vectors + {Queries} query vectors (provider {Provider})",
                        catalogEmbeddings.Count, lineVectors.Count, provider);
                }
                else
                {
                    _logger.LogInformation("No embeddings in store for provider {Provider} — falling back to lexical match", provider);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Embedding context build failed — falling back to lexical match");
            }
        }

        var fallbackMatches = _matcher.Match(recognition.Lines, catalog, embeddingContext);

        // Learning loop: для каждой строки сначала проверяем overrides из БД.
        // Если оператор уже подтверждал эту связку — берём её с Confidence=1.0,
        // минуя Levenshtein. Если нет — используем результат matcher'а.
        var finalMatches = new List<MatchedInvoiceLine>(fallbackMatches.Count);
        var overridesApplied = 0;

        for (var i = 0; i < fallbackMatches.Count; i++)
        {
            var fallback = fallbackMatches[i];
            MatchedInvoiceLine resolved = fallback;

            if (_overrideStore is not null && !string.IsNullOrWhiteSpace(fallback.Source.Name))
            {
                try
                {
                    var overrideMatch = await _overrideStore
                        .FindOverrideAsync(fallback.Source.Name, cancellationToken)
                        .ConfigureAwait(false);

                    if (overrideMatch is not null)
                    {
                        resolved = new MatchedInvoiceLine(
                            Source: fallback.Source,
                            BestMatch: overrideMatch,
                            Alternatives: fallback.BestMatch is not null && fallback.BestMatch.Id != overrideMatch.Id
                                ? new[] { new MatchCandidate(fallback.BestMatch, fallback.Confidence, fallback.Kind) }
                                : Array.Empty<MatchCandidate>(),
                            Kind: MatchKind.Override,
                            Confidence: 1.0);
                        overridesApplied++;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Override lookup failed for line {LineNumber}, falling back to matcher",
                        fallback.Source.LineNumber);
                }
            }

            finalMatches.Add(resolved);
        }

        var matchedCount = finalMatches.Count(m => m.Kind != MatchKind.NoMatch);
        _logger.LogInformation(
            "Matching done: {Matched}/{Total} lines matched ({Overrides} from learning loop)",
            matchedCount,
            finalMatches.Count,
            overridesApplied);

        return new InvoiceRecognitionWithMatches(recognition, finalMatches);
    }
}

public sealed record InvoiceRecognitionWithMatches(
    InvoiceRecognitionResult Recognition,
    IReadOnlyList<MatchedInvoiceLine> Matches);
