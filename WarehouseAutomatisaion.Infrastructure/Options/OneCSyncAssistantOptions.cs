namespace WarehouseAutomatisaion.Infrastructure.Options;

public sealed class OneCSyncAssistantOptions
{
    public const string SectionName = "OneCSyncAssistant";

    public bool Enabled { get; set; } = true;

    public string BaseUrl { get; set; } = "https://xn--b1apclaccz4czb.xn--p1ai/onec-sync";

    public string InventoryAssistantPath { get; set; } = "/v1/assistant/inventory-analysis";

    public string InventoryInsightsPath { get; set; } = "/v1/analytics/inventory-insights";

    public string SeasonalityPath { get; set; } = "/v1/analytics/seasonality";

    /// <summary>Эндпоинт диалога с локальной моделью (Ollama) с поддержкой tool-calling.
    /// Сервер проксирует messages + tools в Ollama; агентный цикл живёт здесь, в Major.</summary>
    public string ChatPath { get; set; } = "/v1/assistant/chat";

    /// <summary>Имя модели Ollama. Пусто → берётся серверная по умолчанию (qwen2.5:7b).</summary>
    public string? Model { get; set; }

    /// <summary>Максимум шагов tool-use в одном ответе (страховка от зацикливания).</summary>
    public int MaxToolIterations { get; set; } = 5;

    public string SourceSystem { get; set; } = "onec-local-file";

    public string SyncToken { get; set; } = string.Empty;

    public int SalesPeriodDays { get; set; } = 365;

    public int SlowCoverDays { get; set; } = 90;

    public int LowCoverDays { get; set; } = 14;

    public int Limit { get; set; } = 15;

    public string? PriceTypeContains { get; set; }

    public int TimeoutSeconds { get; set; } = 300;

    public bool FetchTokenViaSsh { get; set; } = true;

    public string SshHost { get; set; } = "root@147.45.108.97";

    public string SshKeyPath { get; set; } = @"work\.ssh\vps_sync_ed25519";

    public string SshKnownHostsPath { get; set; } = @"work\.ssh\known_hosts";
}
