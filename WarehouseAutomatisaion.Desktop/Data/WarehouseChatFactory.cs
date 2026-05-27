using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data.ChatTools;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 12: factory для AI Chat assistant — собирает IChatService с warehouse tools.
// Если Anthropic.ApiKey не настроен → возвращает null, UI пишет "Claude не настроен".
public static class WarehouseChatFactory
{
    public sealed record ChatBundle(IChatService Chat, DesktopMySqlBackplaneService Backplane);

    public static ChatBundle? TryCreate(ILoggerFactory? loggerFactory = null)
    {
        var aiOptions = TryLoadAiProviders();
        if (aiOptions is null
            || string.IsNullOrWhiteSpace(aiOptions.Anthropic.ApiKey)
            || aiOptions.Anthropic.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            return null;
        }

        loggerFactory ??= NullLoggerFactory.Instance;

        var cellCatalog = new MySqlStorageCellCatalog(backplane);
        var stockLocations = new MySqlStockLocationRepository(backplane);
        var recommender = new CellRecommendationService(stockLocations, cellCatalog);

        var tools = new IChatTool[]
        {
            new QueryStockTool(backplane),
            new FindItemLocationTool(backplane, stockLocations),
            new FindCellContentsTool(cellCatalog, stockLocations),
            new SuggestCellTool(backplane, recommender),
        };

        var chat = new ClaudeChatService(
            new StaticMonitor<AiProvidersOptions>(aiOptions),
            tools,
            loggerFactory.CreateLogger<ClaudeChatService>());

        return new ChatBundle(chat, backplane);
    }

    private static AiProvidersOptions? TryLoadAiProviders()
    {
        var found = AppConfigLocator.TryReadAiProvidersSection();
        if (found is null)
        {
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize<AiProvidersOptions>(
                found.Value.SectionJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private sealed class StaticMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
