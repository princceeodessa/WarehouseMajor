using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 11: OpenAI embeddings (text-embedding-3-small по умолчанию).
// Цена в проде ~$0.02 / 1M токенов. Для каталога 8893 items × ~20 токенов =
// ~178k токенов = $0.0036 на полный пересчёт. Можно запускать сколько угодно раз.
//
// Все ошибки маппятся в InvoiceVisionException (один error-model для всех AI вызовов).
public sealed class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly IOptionsMonitor<AiProvidersOptions> _options;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IOptionsMonitor<AiProvidersOptions> options,
        ILogger<OpenAiEmbeddingService> logger)
    {
        _options = options;
        _logger = logger;
    }

    private string ModelId =>
        string.IsNullOrWhiteSpace(_options.CurrentValue.OpenAi.EmbeddingModel)
            ? "text-embedding-3-small"
            : _options.CurrentValue.OpenAi.EmbeddingModel;

    public string ProviderName => $"openai:{ModelId}";

    public int Dimensions => ModelId.Contains("3-large", StringComparison.OrdinalIgnoreCase) ? 3072 : 1536;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var batch = await EmbedBatchAsync(new[] { text }, cancellationToken);
        return batch[0];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var opts = _options.CurrentValue.OpenAi;
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "OpenAI API key not configured. Set AiProviders:OpenAi:ApiKey in appsettings.local.json.");
        }

        // OpenAI поддерживает до 2048 inputs в одном batch и до 300k токенов на запрос.
        // Берём safe chunk = 256 для длинных русских названий товаров.
        const int ChunkSize = 256;

        var stopwatch = Stopwatch.StartNew();
        var client = new EmbeddingClient(ModelId, opts.ApiKey);

        var result = new List<float[]>(texts.Count);
        var totalTokens = 0;

        try
        {
            for (var offset = 0; offset < texts.Count; offset += ChunkSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var chunk = texts.Skip(offset).Take(ChunkSize).ToList();
                // text-embedding-3 не любит пустые строки — заменяем на " " safe placeholder.
                var safeChunk = chunk.Select(t => string.IsNullOrWhiteSpace(t) ? " " : t).ToList();

                var response = await client.GenerateEmbeddingsAsync(safeChunk, cancellationToken: cancellationToken);
                foreach (var embedding in response.Value)
                {
                    var floats = embedding.ToFloats().ToArray();
                    result.Add(floats);
                }
                totalTokens += response.Value.Usage?.TotalTokenCount ?? 0;
            }

            stopwatch.Stop();
            _logger.LogInformation(
                "OpenAI embeddings: {Texts} texts → {Vectors} vectors, {Duration} ms, {Tokens} tokens, provider={Provider}",
                texts.Count, result.Count, stopwatch.ElapsedMilliseconds, totalTokens, ProviderName);

            return result;
        }
        catch (ClientResultException exception) when (exception.Status == 401)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.AuthenticationFailed,
                "OpenAI authentication failed (embeddings). Check API key.", exception);
        }
        catch (ClientResultException exception) when (exception.Status == 429)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.RateLimited,
                "OpenAI rate limit exceeded (embeddings).", exception);
        }
        catch (ClientResultException exception) when (exception.Status == 402)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.QuotaExceeded,
                "OpenAI billing/quota issue (embeddings).", exception);
        }
        catch (ClientResultException exception) when (exception.Status >= 500)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.ProviderUnavailable,
                $"OpenAI server error ({exception.Status}) (embeddings).", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "Network error calling OpenAI embeddings.", exception);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.NetworkError,
                "OpenAI embeddings request timed out.");
        }
        catch (InvoiceVisionException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvoiceVisionException(
                InvoiceVisionFailureKind.Other,
                $"Unexpected error from OpenAI embeddings: {exception.Message}", exception);
        }
    }
}
