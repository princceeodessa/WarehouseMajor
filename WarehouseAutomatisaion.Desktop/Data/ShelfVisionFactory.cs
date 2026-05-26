using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WarehouseAutomatisaion.Application.Abstractions.Ai;
using WarehouseAutomatisaion.Infrastructure.Ai;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 10: factory для ClaudeShelfInventoryVisionService.
// Использует те же AiProviders настройки что InvoiceRecognitionPipelineFactory
// (один Anthropic key для обоих сервисов).
//
// Если Anthropic.ApiKey не задан или placeholder — возвращает null
// (UI должен показать «Claude не настроен» и отключить AI-кнопки).
public static class ShelfVisionFactory
{
    public static IShelfInventoryVisionService? TryCreate(ILoggerFactory? loggerFactory = null)
    {
        var options = TryLoadAiProviders();
        if (options is null
            || string.IsNullOrWhiteSpace(options.Anthropic.ApiKey)
            || options.Anthropic.ApiKey.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        loggerFactory ??= NullLoggerFactory.Instance;

        return new ClaudeShelfInventoryVisionService(
            new StaticMonitor<AiProvidersOptions>(options),
            loggerFactory.CreateLogger<ClaudeShelfInventoryVisionService>());
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

    private sealed class StaticMonitor<T> : IOptionsMonitor<T>
    {
        private readonly T _value;
        public StaticMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
