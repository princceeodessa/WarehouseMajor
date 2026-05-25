namespace WarehouseAutomatisaion.Infrastructure.Options;

// Sprint 5: конфигурация AI-провайдеров для vision / NLP / прочих фич.
// Поддерживает несколько провайдеров параллельно, активный — по полю Default.
// Реальные ключи живут только в appsettings.local.json (gitignored).
public sealed class AiProvidersOptions
{
    public const string SectionName = "AiProviders";

    /// <summary>"OpenAI" или "Anthropic". Определяет какой провайдер используется по умолчанию.</summary>
    public string Default { get; init; } = "OpenAI";

    public OpenAiProviderOptions OpenAi { get; init; } = new();

    public AnthropicProviderOptions Anthropic { get; init; } = new();

    public bool IsConfigured()
    {
        return Default.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
            ? !string.IsNullOrWhiteSpace(OpenAi.ApiKey)
            : !string.IsNullOrWhiteSpace(Anthropic.ApiKey);
    }
}

public sealed class OpenAiProviderOptions
{
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Модель. По умолчанию gpt-4o (multi-modal, vision-capable).</summary>
    public string Model { get; init; } = "gpt-4o";

    /// <summary>Базовый URL API. Кастомный нужен только для прокси / Azure OpenAI.</summary>
    public string? BaseUrl { get; init; }

    public int MaxTokens { get; init; } = 4096;

    public int TimeoutSeconds { get; init; } = 60;
}

public sealed class AnthropicProviderOptions
{
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>Модель Claude. По умолчанию sonnet-4.5 — оптимально по цене/качеству для vision.</summary>
    public string Model { get; init; } = "claude-sonnet-4-5";

    public int MaxTokens { get; init; } = 4096;

    /// <summary>Prompt caching на статичных system-промптах — экономия токенов на повторных запросах.</summary>
    public bool EnableCaching { get; init; } = true;

    public int TimeoutSeconds { get; init; } = 60;
}
