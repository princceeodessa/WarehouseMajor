using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;

namespace WarehouseAutomatisaion.Application.Services;

// Sprint 5 Task 17: orchestrator связывает три кубика
//   IInvoiceVisionService   — распознавание фото
//   InvoiceLineMatcher      — sопоставление строк с номенклатурой
//   INomenclatureCatalogReader — загрузка каталога для matcher'а
// Используется UI и smoke-tool. Один публичный метод.
public sealed class InvoiceRecognitionService
{
    private readonly IInvoiceVisionService _vision;
    private readonly INomenclatureCatalogReader _catalog;
    private readonly InvoiceLineMatcher _matcher;
    private readonly ILogger<InvoiceRecognitionService> _logger;

    public InvoiceRecognitionService(
        IInvoiceVisionService vision,
        INomenclatureCatalogReader catalog,
        InvoiceLineMatcher matcher,
        ILogger<InvoiceRecognitionService> logger)
    {
        _vision = vision;
        _catalog = catalog;
        _matcher = matcher;
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

        var matches = _matcher.Match(recognition.Lines, catalog);

        var matchedCount = matches.Count(m => m.Kind != MatchKind.NoMatch);
        _logger.LogInformation(
            "Matching done: {Matched}/{Total} lines matched",
            matchedCount,
            matches.Count);

        return new InvoiceRecognitionWithMatches(recognition, matches);
    }
}

public sealed record InvoiceRecognitionWithMatches(
    InvoiceRecognitionResult Recognition,
    IReadOnlyList<MatchedInvoiceLine> Matches);
