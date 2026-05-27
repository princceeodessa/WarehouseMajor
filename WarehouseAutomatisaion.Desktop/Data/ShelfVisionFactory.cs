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
