using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Application.Services;
using WarehouseAutomatisaion.Desktop.Data.ChatTools;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Ai.Tools;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Desktop.Data;

public static class WarehouseChatFactory
{
    public sealed record ChatBundle(IChatService Chat, DesktopMySqlBackplaneService? Backplane);

    public static ChatBundle? TryCreate(ILoggerFactory? loggerFactory = null)
    {
        var options = TryLoadOneCSyncAssistantOptions() ?? new OneCSyncAssistantOptions();
        if (!options.Enabled)
        {
            return null;
        }

        ResolveKnownSshPaths(options);
        loggerFactory ??= NullLoggerFactory.Instance;

        var tokenResolver = new OneCSyncTokenResolver(
            options,
            loggerFactory.CreateLogger<OneCSyncTokenResolver>());

        var analyticsHttp = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 15, 600))
        };
        var analyticsClient = new OneCSyncAnalyticsClient(
            options,
            tokenResolver,
            analyticsHttp,
            loggerFactory.CreateLogger<OneCSyncAnalyticsClient>());

        var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
        var tools = CreateWarehouseTools(backplane, analyticsClient, options);

        var chat = new OneCSyncAssistantChatService(
            options,
            tools,
            tokenResolver,
            loggerFactory.CreateLogger<OneCSyncAssistantChatService>());

        return new ChatBundle(chat, backplane);
    }

    private static IReadOnlyList<IChatTool> CreateWarehouseTools(
        DesktopMySqlBackplaneService? backplane,
        OneCSyncAnalyticsClient analyticsClient,
        OneCSyncAssistantOptions options)
    {
        // Аналитика по запасам/сезонности доступна всегда (это API, не MySQL Major).
        var tools = new List<IChatTool>
        {
            new InventoryInsightsTool(analyticsClient, options),
            new SeasonalityTool(analyticsClient, options)
        };

        // Точные WMS-инструменты включаются только при подключении MySQL Major.
        if (backplane is not null)
        {
            var cellCatalog = new MySqlStorageCellCatalog(backplane);
            var stockLocations = new MySqlStockLocationRepository(backplane);
            var recommender = new CellRecommendationService(stockLocations, cellCatalog);

            tools.Add(new QueryStockTool(backplane));
            tools.Add(new FindItemLocationTool(backplane, stockLocations));
            tools.Add(new FindCellContentsTool(cellCatalog, stockLocations));
            tools.Add(new SuggestCellTool(backplane, recommender));
        }

        return tools;
    }

    private static OneCSyncAssistantOptions? TryLoadOneCSyncAssistantOptions()
    {
        var found = AppConfigLocator.TryReadSection(OneCSyncAssistantOptions.SectionName);
        if (found is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<OneCSyncAssistantOptions>(
                found.Value.SectionJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private static void ResolveKnownSshPaths(OneCSyncAssistantOptions options)
    {
        if (!options.FetchTokenViaSsh)
        {
            return;
        }

        if (!File.Exists(Environment.ExpandEnvironmentVariables(options.SshKeyPath)))
        {
            var discoveredKey = FindCodexSshFile("vps_sync_ed25519");
            if (!string.IsNullOrWhiteSpace(discoveredKey))
            {
                options.SshKeyPath = discoveredKey;
            }
        }

        if (!File.Exists(Environment.ExpandEnvironmentVariables(options.SshKnownHostsPath)))
        {
            var discoveredKnownHosts = FindCodexSshFile("known_hosts");
            if (!string.IsNullOrWhiteSpace(discoveredKnownHosts))
            {
                options.SshKnownHostsPath = discoveredKnownHosts;
            }
        }
    }

    private static string? FindCodexSshFile(string fileName)
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(documents))
        {
            return null;
        }

        var codexRoot = Path.Combine(documents, "Codex");
        if (!Directory.Exists(codexRoot))
        {
            return null;
        }

        var preferred = Path.Combine(
            codexRoot,
            "2026-06-04",
            "1-vps-api-codex-antropic",
            "work",
            ".ssh",
            fileName);
        if (File.Exists(preferred))
        {
            return preferred;
        }

        try
        {
            return Directory
                .EnumerateFiles(codexRoot, fileName, SearchOption.AllDirectories)
                .FirstOrDefault(path => path.Contains($"{Path.DirectorySeparatorChar}work{Path.DirectorySeparatorChar}.ssh{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return null;
        }
    }
}
