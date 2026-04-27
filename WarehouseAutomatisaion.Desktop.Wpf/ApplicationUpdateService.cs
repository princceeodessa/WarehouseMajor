using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal sealed class ApplicationUpdateService : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ApplicationUpdateOptions _options;

    public ApplicationUpdateService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        _options = ApplicationUpdateSettings.Snapshot();
    }

    public string CurrentVersion => AppBranding.CurrentVersion;

    public bool IsEnabled => _options.Enabled;

    public bool IsConfigured => _options.IsConfigured;

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    public async Task<ApplicationUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return ApplicationUpdateCheckResult.Disabled("Автообновление отключено в конфиге приложения.");
        }

        if (!IsConfigured)
        {
            return ApplicationUpdateCheckResult.Disabled(
                "Обновления не настроены. Заполните ApplicationUpdate: GitHubOwner, GitHubRepository и AssetName.");
        }

        if (!TryParseVersion(CurrentVersion, out var currentVersion))
        {
            return ApplicationUpdateCheckResult.Failed(
                $"Не удалось определить текущую версию приложения {CurrentVersion}.");
        }

        var requestUri =
            $"https://api.github.com/repos/{Uri.EscapeDataString(_options.GitHubOwner)}/{Uri.EscapeDataString(_options.GitHubRepository)}/releases/latest";

        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("User-Agent", AppBranding.GitHubUserAgent);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return ApplicationUpdateCheckResult.Failed(
                $"В GitHub-репозитории {_options.GitHubOwner}/{_options.GitHubRepository} пока нет release-версий.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return ApplicationUpdateCheckResult.Failed(
                "GitHub временно отклонил запрос на проверку обновления. Проверьте доступность релизов и лимиты API.");
        }

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var tagName = GetString(root, "tag_name") ?? GetString(root, "name");
        if (!TryParseVersion(tagName, out var releaseVersion))
        {
            return ApplicationUpdateCheckResult.Failed("Не удалось разобрать версию последнего GitHub release.");
        }

        var asset = FindAsset(root, _options.AssetName);
        if (asset is null)
        {
            return ApplicationUpdateCheckResult.Failed(
                $"В релизе не найден архив {_options.AssetName}. Проверьте workflow публикации.");
        }

        var downloadUrl = GetString(asset.Value, "browser_download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return ApplicationUpdateCheckResult.Failed("У релиз-архива отсутствует browser_download_url.");
        }

        var release = new ApplicationRelease(
            Version: FormatVersion(releaseVersion),
            Tag: tagName ?? FormatVersion(releaseVersion),
            DownloadUrl: downloadUrl,
            PageUrl: GetString(root, "html_url") ?? string.Empty,
            AssetName: GetString(asset.Value, "name") ?? _options.AssetName);

        if (releaseVersion <= currentVersion)
        {
            return ApplicationUpdateCheckResult.UpToDate(
                $"Установлена актуальная версия {CurrentVersion}.",
                release);
        }

        return ApplicationUpdateCheckResult.UpdateAvailable(
            $"Доступна версия {release.Version}. Текущая версия: {CurrentVersion}.",
            release);
    }

    public async Task<ApplicationUpdateLaunchResult> PrepareAndLaunchUpdateAsync(
        ApplicationRelease release,
        CancellationToken cancellationToken = default)
    {
        try
        {
            EnsureApplicationDirectoryWritable();

            var tempRoot = Path.Combine(
                Path.GetTempPath(),
                AppBranding.ProductName,
                "updates",
                $"{release.Version}-{Guid.NewGuid():N}");

            var archivePath = Path.Combine(tempRoot, release.AssetName);
            var extractPath = Path.Combine(tempRoot, "package");
            var scriptPath = Path.Combine(tempRoot, "apply-update.cmd");
            var logPath = Path.Combine(tempRoot, "update.log");

            Directory.CreateDirectory(tempRoot);

            await DownloadFileAsync(release.DownloadUrl, archivePath, cancellationToken);
            ZipFile.ExtractToDirectory(archivePath, extractPath);

            var executableName = AppBranding.ExecutableName;
            var extractedExecutable = Path.Combine(extractPath, executableName);
            if (!File.Exists(extractedExecutable))
            {
                return ApplicationUpdateLaunchResult.Failed(
                    $"В архиве обновления нет файла {executableName}. Проверьте содержимое release-архива.");
            }

            var targetDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var targetExecutable = Path.Combine(targetDirectory, executableName);
            var currentProcessId = Process.GetCurrentProcess().Id;

            var script = BuildUpdateScript(
                currentProcessId,
                tempRoot,
                extractPath,
                targetDirectory,
                targetExecutable,
                logPath);

            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"\"{scriptPath}\"\"",
                WorkingDirectory = tempRoot,
                CreateNoWindow = true,
                UseShellExecute = false
            });

            return ApplicationUpdateLaunchResult.Success(
                $"Обновление до версии {release.Version} подготовлено. Приложение будет закрыто и запущено заново.");
        }
        catch (Exception exception)
        {
            return ApplicationUpdateLaunchResult.Failed(
                $"Не удалось подготовить обновление: {exception.Message}");
        }
    }

    private static JsonElement? FindAsset(JsonElement root, string assetName)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        JsonElement? fallbackZip = null;
        foreach (var asset in assets.EnumerateArray())
        {
            var currentName = GetString(asset, "name");
            if (string.Equals(currentName, assetName, StringComparison.OrdinalIgnoreCase))
            {
                return asset;
            }

            if (fallbackZip is null && currentName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
            {
                fallbackZip = asset;
            }
        }

        return fallbackZip;
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool TryParseVersion(string? rawValue, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return false;
        }

        var trimmed = rawValue.Trim();
        if (trimmed.StartsWith("v", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed[1..];
        }

        var numericPart = new string(trimmed.TakeWhile(character => char.IsDigit(character) || character == '.').ToArray());
        if (string.IsNullOrWhiteSpace(numericPart))
        {
            return false;
        }

        var parts = numericPart
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4)
            .ToList();

        while (parts.Count < 4)
        {
            parts.Add("0");
        }

        if (Version.TryParse(string.Join('.', parts), out var parsedVersion) && parsedVersion is not null)
        {
            version = parsedVersion;
            return true;
        }

        return false;
    }

    private static string FormatVersion(Version version)
    {
        return $"{version.Major}.{version.Minor}.{Math.Max(version.Build, 0)}";
    }

    private static void EnsureApplicationDirectoryWritable()
    {
        var probePath = Path.Combine(AppContext.BaseDirectory, $".{Guid.NewGuid():N}.write-probe");
        try
        {
            File.WriteAllText(probePath, "probe", Encoding.UTF8);
            File.Delete(probePath);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Папка приложения недоступна для записи ({AppContext.BaseDirectory}). Переместите программу в обычную рабочую папку или запустите с правами на запись. {exception.Message}",
                exception);
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("User-Agent", AppBranding.GitHubUserAgent);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = File.Create(destinationPath);
        await responseStream.CopyToAsync(fileStream, cancellationToken);
    }

    private static string BuildUpdateScript(
        int processId,
        string tempRoot,
        string sourceDirectory,
        string targetDirectory,
        string targetExecutable,
        string logPath)
    {
        var script = new StringBuilder();
        script.AppendLine("@echo off");
        script.AppendLine("setlocal EnableExtensions");
        script.AppendLine($"set \"APP_PID={processId}\"");
        script.AppendLine($"set \"TEMP_ROOT={EscapeBatchValue(tempRoot)}\"");
        script.AppendLine($"set \"SOURCE_DIR={EscapeBatchValue(sourceDirectory)}\"");
        script.AppendLine($"set \"TARGET_DIR={EscapeBatchValue(targetDirectory)}\"");
        script.AppendLine($"set \"TARGET_EXE={EscapeBatchValue(targetExecutable)}\"");
        script.AppendLine($"set \"LOG_FILE={EscapeBatchValue(logPath)}\"");
        script.AppendLine("call :log Starting desktop update");
        script.AppendLine(":wait_for_exit");
        script.AppendLine("tasklist /FI \"PID eq %APP_PID%\" | find \"%APP_PID%\" >nul");
        script.AppendLine("if not errorlevel 1 (");
        script.AppendLine("    timeout /t 1 /nobreak >nul");
        script.AppendLine("    goto wait_for_exit");
        script.AppendLine(")");
        script.AppendLine("call :log Copying release files");
        script.AppendLine("robocopy \"%SOURCE_DIR%\" \"%TARGET_DIR%\" /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP /XF appsettings.local.json /XD app_data >nul");
        script.AppendLine("set \"ROBOCOPY_EXIT=%ERRORLEVEL%\"");
        script.AppendLine("if %ROBOCOPY_EXIT% GEQ 8 (");
        script.AppendLine("    call :log Robocopy failed with exit code %ROBOCOPY_EXIT%");
        script.AppendLine("    goto end");
        script.AppendLine(")");
        script.AppendLine("call :log Launching updated application");
        script.AppendLine("start \"\" \"%TARGET_EXE%\"");
        script.AppendLine("call :log Scheduling temp cleanup");
        script.AppendLine("start \"\" /b cmd /c \"timeout /t 5 /nobreak >nul & rd /s /q \"\"%TEMP_ROOT%\"\"\"");
        script.AppendLine("exit /b 0");
        script.AppendLine(":log");
        script.AppendLine(">> \"%LOG_FILE%\" echo [%date% %time%] %*");
        script.AppendLine("exit /b 0");
        script.AppendLine(":end");
        script.AppendLine("exit /b %ROBOCOPY_EXIT%");
        return script.ToString();
    }

    private static string EscapeBatchValue(string value)
    {
        return value.Replace("%", "%%");
    }
}

internal sealed record ApplicationRelease(
    string Version,
    string Tag,
    string DownloadUrl,
    string PageUrl,
    string AssetName);

internal enum ApplicationUpdateCheckState
{
    Disabled,
    UpToDate,
    UpdateAvailable,
    Failed
}

internal sealed record ApplicationUpdateCheckResult(
    ApplicationUpdateCheckState State,
    string Message,
    ApplicationRelease? Release = null)
{
    public static ApplicationUpdateCheckResult Disabled(string message)
        => new(ApplicationUpdateCheckState.Disabled, message);

    public static ApplicationUpdateCheckResult UpToDate(string message, ApplicationRelease? release = null)
        => new(ApplicationUpdateCheckState.UpToDate, message, release);

    public static ApplicationUpdateCheckResult UpdateAvailable(string message, ApplicationRelease release)
        => new(ApplicationUpdateCheckState.UpdateAvailable, message, release);

    public static ApplicationUpdateCheckResult Failed(string message)
        => new(ApplicationUpdateCheckState.Failed, message);
}

internal sealed record ApplicationUpdateLaunchResult(
    bool IsSuccess,
    string Message)
{
    public static ApplicationUpdateLaunchResult Success(string message)
        => new(true, message);

    public static ApplicationUpdateLaunchResult Failed(string message)
        => new(false, message);
}
