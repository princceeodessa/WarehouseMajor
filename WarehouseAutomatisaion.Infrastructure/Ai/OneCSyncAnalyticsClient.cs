using System.Text;
using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 14: тонкий GET-клиент к analytics-эндпоинтам 1C/Ollama API
// (/v1/analytics/*). Используется инструментами чата inventory_insights
// и seasonality. Токен берётся через общий OneCSyncTokenResolver.
public sealed class OneCSyncAnalyticsClient
{
    private readonly OneCSyncAssistantOptions _options;
    private readonly OneCSyncTokenResolver _tokenResolver;
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    public OneCSyncAnalyticsClient(
        OneCSyncAssistantOptions options,
        OneCSyncTokenResolver tokenResolver,
        HttpClient http,
        ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _tokenResolver = tokenResolver ?? throw new ArgumentNullException(nameof(tokenResolver));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetJsonAsync(
        string path,
        IReadOnlyDictionary<string, string?> query,
        CancellationToken cancellationToken)
    {
        var token = await _tokenResolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Не найден X-Sync-Token для analytics API.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(path, query));
        request.Headers.TryAddWithoutValidation("X-Sync-Token", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Analytics {Path} вернул {Status}: {Body}", path, (int)response.StatusCode, body);
            throw new HttpRequestException($"Analytics {path} {(int)response.StatusCode}: {body}");
        }

        return body;
    }

    private Uri BuildUri(string path, IReadOnlyDictionary<string, string?> query)
    {
        var baseUrl = string.IsNullOrWhiteSpace(_options.BaseUrl)
            ? "https://xn--b1apclaccz4czb.xn--p1ai/onec-sync"
            : _options.BaseUrl.Trim().TrimEnd('/');
        var normalizedPath = "/" + (path ?? string.Empty).TrimStart('/');
        var builder = new StringBuilder(baseUrl).Append(normalizedPath);

        var pairs = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}")
            .ToArray();

        if (pairs.Length > 0)
        {
            builder.Append('?').Append(string.Join("&", pairs));
        }

        return new Uri(builder.ToString(), UriKind.Absolute);
    }
}
