using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Vision;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 5 Task 13: factory чтобы UI окно не утопало в ручной wiring зависимостей.
// Читает AiProviders из appsettings.local.json (тот же файл что RemoteDatabase),
// создаёт OpenAiInvoiceVisionService + catalog reader + matcher + orchestrator.
//
// Не singleton — каждый вызов создаёт свежие зависимости. В UI это safe потому что
// окно живёт коротко (открыли → распознали → сохранили → закрыли).
public static class InvoiceRecognitionPipelineFactory
{
    public sealed record Pipeline(
        InvoiceRecognitionService Orchestrator,
        IReceiptDraftWriter DraftWriter,
        IInvoiceMatchOverrideStore OverrideStore,
        DesktopMySqlBackplaneService Backplane,
        string ProviderName,
        IEmbeddingService EmbeddingService,
        INomenclatureEmbeddingStore EmbeddingStore,
        INomenclatureCatalogReader CatalogReader);

    public static Pipeline? TryCreate(ILoggerFactory? loggerFactory = null)
    {
        var aiOptions = TryLoadAiProviders();
        if (aiOptions is null
            || string.IsNullOrWhiteSpace(aiOptions.OpenAi.ApiKey)
            || aiOptions.OpenAi.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            return null;
        }

        loggerFactory ??= NullLoggerFactory.Instance;

        var visionService = new OpenAiInvoiceVisionService(
            new StaticOptionsMonitor<AiProvidersOptions>(aiOptions),
            loggerFactory.CreateLogger<OpenAiInvoiceVisionService>());

        var catalogReader = new MySqlNomenclatureCatalogReader(backplane);
        var matcher = new InvoiceLineMatcher();
        var overrideStore = new MySqlInvoiceMatchOverrideStore(backplane);

        // Sprint 11: подключаем embedding-сервис и store. Они опциональны для
        // orchestrator'а (graceful fallback если в БД ещё нет embeddings).
        var embeddingService = new OpenAiEmbeddingService(
            new StaticOptionsMonitor<AiProvidersOptions>(aiOptions),
            loggerFactory.CreateLogger<OpenAiEmbeddingService>());
        var embeddingStore = new MySqlNomenclatureEmbeddingStore(backplane);

        var orchestrator = new InvoiceRecognitionService(
            visionService,
            catalogReader,
            matcher,
            loggerFactory.CreateLogger<InvoiceRecognitionService>(),
            overrideStore,
            embeddingService,
            embeddingStore);

        var draftWriter = new MySqlReceiptDraftWriter(backplane);

        return new Pipeline(
            orchestrator, draftWriter, overrideStore, backplane,
            visionService.ProviderName,
            embeddingService, embeddingStore, catalogReader);
    }

    private static AiProvidersOptions? TryLoadAiProviders()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"),
            Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "WarehouseAutomatisaion.Desktop.Wpf", "appsettings.local.json")),
        };

        foreach (var path in candidates)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (!document.RootElement.TryGetProperty("AiProviders", out var section))
                {
                    continue;
                }

                return JsonSerializer.Deserialize<AiProvidersOptions>(
                    section.GetRawText(),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
    }

    private sealed class StaticOptionsMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
