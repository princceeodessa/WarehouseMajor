namespace WarehouseAutomatisaion.Infrastructure.Options;

public sealed class OneCIntegrationOptions
{
    public const string SectionName = "Integrations:OneC";

    public string BaseUrl { get; init; } = string.Empty;

    public string ApiKey { get; init; } = string.Empty;

    public bool AutoSyncEnabled { get; init; }

    public bool SimulateResponses { get; init; } = true;

    public int SyncIntervalMinutes { get; init; } = 15;
}
