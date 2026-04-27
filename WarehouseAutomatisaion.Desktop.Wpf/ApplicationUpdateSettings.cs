using System.IO;
using System.Text.Json;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class ApplicationUpdateSettings
{
    private static readonly object Sync = new();
    private static ApplicationUpdateOptions? _cached;

    public static ApplicationUpdateOptions Snapshot()
    {
        lock (Sync)
        {
            _cached ??= Load();
            return _cached.Clone();
        }
    }

    private static ApplicationUpdateOptions Load()
    {
        var options = new ApplicationUpdateOptions();
        MergeFromFile(options, Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        MergeFromFile(options, Path.Combine(AppContext.BaseDirectory, "appsettings.local.json"));
        MergeFromEnvironment(options);
        return options;
    }

    private static void MergeFromFile(ApplicationUpdateOptions target, string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        if (!document.RootElement.TryGetProperty("ApplicationUpdate", out var section))
        {
            return;
        }

        if (TryGetBoolean(section, "Enabled", out var enabled))
        {
            target.Enabled = enabled;
        }

        target.GitHubOwner = GetString(section, "GitHubOwner") ?? target.GitHubOwner;
        target.GitHubRepository = GetString(section, "GitHubRepository") ?? target.GitHubRepository;
        target.AssetName = GetString(section, "AssetName") ?? target.AssetName;
    }

    private static void MergeFromEnvironment(ApplicationUpdateOptions target)
    {
        var enabledRaw = Environment.GetEnvironmentVariable("MAJORWAREHAUSE_UPDATE_ENABLED");
        if (!string.IsNullOrWhiteSpace(enabledRaw) && bool.TryParse(enabledRaw, out var enabled))
        {
            target.Enabled = enabled;
        }

        var owner = Environment.GetEnvironmentVariable("MAJORWAREHAUSE_UPDATE_GITHUB_OWNER");
        var repository = Environment.GetEnvironmentVariable("MAJORWAREHAUSE_UPDATE_GITHUB_REPOSITORY");
        var assetName = Environment.GetEnvironmentVariable("MAJORWAREHAUSE_UPDATE_ASSET_NAME");

        if (!string.IsNullOrWhiteSpace(owner))
        {
            target.GitHubOwner = owner;
            target.Enabled = true;
        }

        if (!string.IsNullOrWhiteSpace(repository))
        {
            target.GitHubRepository = repository;
            target.Enabled = true;
        }

        if (!string.IsNullOrWhiteSpace(assetName))
        {
            target.AssetName = assetName;
        }
    }

    private static string? GetString(JsonElement section, string propertyName)
    {
        return section.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryGetBoolean(JsonElement section, string propertyName, out bool value)
    {
        value = false;
        if (!section.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.True || property.ValueKind == JsonValueKind.False)
        {
            value = property.GetBoolean();
            return true;
        }

        if (property.ValueKind == JsonValueKind.String && bool.TryParse(property.GetString(), out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}

internal sealed class ApplicationUpdateOptions
{
    public bool Enabled { get; set; }

    public string GitHubOwner { get; set; } = string.Empty;

    public string GitHubRepository { get; set; } = string.Empty;

    public string AssetName { get; set; } = AppBranding.ReleaseAssetName;

    public bool IsConfigured =>
        Enabled
        && !string.IsNullOrWhiteSpace(GitHubOwner)
        && !string.IsNullOrWhiteSpace(GitHubRepository)
        && !string.IsNullOrWhiteSpace(AssetName);

    public ApplicationUpdateOptions Clone()
    {
        return new ApplicationUpdateOptions
        {
            Enabled = Enabled,
            GitHubOwner = GitHubOwner,
            GitHubRepository = GitHubRepository,
            AssetName = AssetName
        };
    }
}
