using System.IO;
using System.Reflection;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class AppBranding
{
    public const string ProductName = "MajorWarehause";
    public const string ReleaseAssetName = "majorwarehause-win-x64.zip";

    public static string MessageBoxTitle => ProductName;

    public static string CurrentVersion { get; } = ResolveCurrentVersion();

    public static string GitHubUserAgent => $"{ProductName}/{CurrentVersion}";

    public static string ExecutableName => Path.GetFileName(Environment.ProcessPath) ?? $"{ProductName}.exe";

    private static string ResolveCurrentVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version
            ?? Assembly.GetExecutingAssembly().GetName().Version
            ?? new Version(1, 0, 0, 0);

        var build = version.Build < 0 ? 0 : version.Build;
        return $"{version.Major}.{version.Minor}.{build}";
    }
}
