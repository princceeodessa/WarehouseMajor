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
            $"https://api.github.com/repos/{Uri.EscapeDataString(_options.GitHubOwner)}/{Uri.EscapeDataString(_options.GitHubRepository)}/releases?per_page=30";

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
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return ApplicationUpdateCheckResult.Failed("GitHub вернул неожиданный формат списка релизов.");
        }

        Version? releaseVersion = null;
        ApplicationRelease? release = null;
        foreach (var releaseElement in document.RootElement.EnumerateArray())
        {
            if (GetBoolean(releaseElement, "draft") || GetBoolean(releaseElement, "prerelease"))
            {
                continue;
            }

            if (!TryCreateRelease(releaseElement, out var candidateVersion, out var candidateRelease))
            {
                continue;
            }

            if (releaseVersion is null || candidateVersion > releaseVersion)
            {
                releaseVersion = candidateVersion;
                release = candidateRelease;
            }
        }

        if (releaseVersion is null || release is null)
        {
            return ApplicationUpdateCheckResult.Failed(
                $"В GitHub-релизах не найден архив {_options.AssetName}. Проверьте workflow публикации.");
        }

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

    private bool TryCreateRelease(
        JsonElement releaseElement,
        out Version releaseVersion,
        out ApplicationRelease? release)
    {
        releaseVersion = new Version(0, 0, 0, 0);
        release = null;

        var tagName = GetString(releaseElement, "tag_name") ?? GetString(releaseElement, "name");
        if (!TryParseVersion(tagName, out releaseVersion))
        {
            return false;
        }

        var asset = FindAsset(releaseElement, _options.AssetName);
        if (asset is null)
        {
            return false;
        }

        var downloadUrl = GetString(asset.Value, "browser_download_url");
        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            return false;
        }

        release = new ApplicationRelease(
            Version: FormatVersion(releaseVersion),
            Tag: tagName ?? FormatVersion(releaseVersion),
            DownloadUrl: downloadUrl,
            PageUrl: GetString(releaseElement, "html_url") ?? string.Empty,
            AssetName: GetString(asset.Value, "name") ?? _options.AssetName);
        return true;
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
            var scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
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
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                WorkingDirectory = tempRoot,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
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

    private static bool GetBoolean(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.True;
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
        var build = Math.Max(version.Build, 0);
        var revision = Math.Max(version.Revision, 0);
        return revision > 0
            ? $"{version.Major}.{version.Minor}.{build}.{revision}"
            : $"{version.Major}.{version.Minor}.{build}";
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
        var persistentLogPath = Path.Combine(targetDirectory, "app_data", "desktop-update.log");
        var executableNameWithoutExtension = Path.GetFileNameWithoutExtension(targetExecutable);
        var script = new StringBuilder();
        script.AppendLine("$ErrorActionPreference = 'Stop'");
        script.AppendLine("$ProgressPreference = 'SilentlyContinue'");
        script.AppendLine($"$appPid = {processId}");
        script.AppendLine($"$tempRoot = {ToPowerShellSingleQuotedString(tempRoot)}");
        script.AppendLine($"$sourceDir = {ToPowerShellSingleQuotedString(sourceDirectory)}");
        script.AppendLine($"$targetDir = {ToPowerShellSingleQuotedString(targetDirectory)}");
        script.AppendLine($"$targetExe = {ToPowerShellSingleQuotedString(targetExecutable)}");
        script.AppendLine($"$tempLog = {ToPowerShellSingleQuotedString(logPath)}");
        script.AppendLine($"$persistentLog = {ToPowerShellSingleQuotedString(persistentLogPath)}");
        script.AppendLine($"$targetProcessName = {ToPowerShellSingleQuotedString(executableNameWithoutExtension)}");
        script.AppendLine();
        script.AppendLine("function Write-UpdateLog {");
        script.AppendLine("    param([string]$Message)");
        script.AppendLine("    $line = ('[{0:yyyy-MM-dd HH:mm:ss.fff zzz}] {1}' -f [DateTimeOffset]::Now, $Message)");
        script.AppendLine("    foreach ($path in @($tempLog, $persistentLog)) {");
        script.AppendLine("        try {");
        script.AppendLine("            $directory = Split-Path -Parent $path");
        script.AppendLine("            if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Path $directory -Force | Out-Null }");
        script.AppendLine("            Add-Content -LiteralPath $path -Value $line -Encoding UTF8");
        script.AppendLine("        } catch { }");
        script.AppendLine("    }");
        script.AppendLine("}");
        script.AppendLine();
        script.AppendLine("try {");
        script.AppendLine("    Write-UpdateLog 'Starting desktop update'");
        script.AppendLine("    try {");
        script.AppendLine("        $currentProcess = Get-Process -Id $appPid -ErrorAction SilentlyContinue");
        script.AppendLine("        if ($null -ne $currentProcess) {");
        script.AppendLine("            Write-UpdateLog \"Waiting for current process $appPid to exit\"");
        script.AppendLine("            Wait-Process -Id $appPid -Timeout 120 -ErrorAction SilentlyContinue");
        script.AppendLine("        }");
        script.AppendLine("    } catch {");
        script.AppendLine("        Write-UpdateLog \"Wait for current process failed: $($_.Exception.Message)\"");
        script.AppendLine("    }");
        script.AppendLine();
        script.AppendLine("    Start-Sleep -Milliseconds 500");
        script.AppendLine("    $remainingProcesses = Get-Process -Name $targetProcessName -ErrorAction SilentlyContinue | Where-Object { $_.Id -ne $PID }");
        script.AppendLine("    foreach ($process in $remainingProcesses) {");
        script.AppendLine("        Write-UpdateLog \"Stopping remaining process $($process.Id) $($process.ProcessName)\"");
        script.AppendLine("        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue");
        script.AppendLine("    }");
        script.AppendLine("    Start-Sleep -Milliseconds 700");
        script.AppendLine();
        script.AppendLine("    if (-not (Test-Path -LiteralPath $sourceDir)) { throw \"Source package directory was not found: $sourceDir\" }");
        script.AppendLine("    if (-not (Test-Path -LiteralPath $targetDir)) { New-Item -ItemType Directory -Path $targetDir -Force | Out-Null }");
        script.AppendLine("    Write-UpdateLog \"Copying release files from $sourceDir to $targetDir\"");
        script.AppendLine("    $robocopyArgs = @(");
        script.AppendLine("        $sourceDir,");
        script.AppendLine("        $targetDir,");
        script.AppendLine("        '/E',");
        script.AppendLine("        '/R:5',");
        script.AppendLine("        '/W:1',");
        script.AppendLine("        '/NFL',");
        script.AppendLine("        '/NDL',");
        script.AppendLine("        '/NP',");
        script.AppendLine("        '/XF',");
        script.AppendLine("        'appsettings.local.json',");
        script.AppendLine("        '/XD',");
        script.AppendLine("        'app_data'");
        script.AppendLine("    )");
        script.AppendLine("    & robocopy @robocopyArgs 2>&1 | ForEach-Object { Write-UpdateLog $_ }");
        script.AppendLine("    $robocopyExit = $LASTEXITCODE");
        script.AppendLine("    Write-UpdateLog \"Robocopy exit code: $robocopyExit\"");
        script.AppendLine("    if ($robocopyExit -ge 8) { throw \"Robocopy failed with exit code $robocopyExit\" }");
        script.AppendLine();
        script.AppendLine("    $sourceLocalSettings = Join-Path $sourceDir 'appsettings.local.json'");
        script.AppendLine("    $targetLocalSettings = Join-Path $targetDir 'appsettings.local.json'");
        script.AppendLine("    function Test-WarehouseLocalSettingsNeedsRepair {");
        script.AppendLine("        param([string]$Path)");
        script.AppendLine("        if (-not (Test-Path -LiteralPath $Path)) { return $true }");
        script.AppendLine("        try {");
        script.AppendLine("            $settings = Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json");
        script.AppendLine("            if ($null -eq $settings.RemoteDatabase) { return $true }");
        script.AppendLine("            $remote = $settings.RemoteDatabase");
        script.AppendLine("            $hostValue = [string]$remote.Host");
        script.AppendLine("            $databaseValue = [string]$remote.Database");
        script.AppendLine("            $userValue = [string]$remote.User");
        script.AppendLine("            $passwordValue = [string]$remote.Password");
        script.AppendLine("            if ([string]::IsNullOrWhiteSpace($hostValue) -or $hostValue -ieq 'db.example.com') { return $true }");
        script.AppendLine("            if ([string]::IsNullOrWhiteSpace($databaseValue)) { return $true }");
        script.AppendLine("            if ([string]::IsNullOrWhiteSpace($userValue) -or $userValue -ieq 'warehouse_app') { return $true }");
        script.AppendLine("            if ([string]::IsNullOrWhiteSpace($passwordValue) -or $passwordValue -ieq 'change-me' -or $passwordValue -ieq 'configured-by-release-secret') { return $true }");
        script.AppendLine("            return $false");
        script.AppendLine("        } catch {");
        script.AppendLine("            return $true");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine("    if (Test-Path -LiteralPath $sourceLocalSettings) {");
        script.AppendLine("        if (Test-WarehouseLocalSettingsNeedsRepair -Path $targetLocalSettings) {");
        script.AppendLine("            Write-UpdateLog 'Repairing appsettings.local.json from release package'");
        script.AppendLine("            Copy-Item -LiteralPath $sourceLocalSettings -Destination $targetLocalSettings -Force");
        script.AppendLine("        } else {");
        script.AppendLine("            Write-UpdateLog 'Keeping existing appsettings.local.json'");
        script.AppendLine("        }");
        script.AppendLine("    }");
        script.AppendLine();
        script.AppendLine("    if (-not (Test-Path -LiteralPath $targetExe)) { throw \"Updated executable was not found: $targetExe\" }");
        script.AppendLine("    Write-UpdateLog \"Launching updated application: $targetExe\"");
        script.AppendLine("    Start-Process -FilePath $targetExe -WorkingDirectory $targetDir");
        script.AppendLine("    Write-UpdateLog 'Update completed successfully'");
        script.AppendLine();
        script.AppendLine("    $cleanupCommand = \"Start-Sleep -Seconds 8; Remove-Item -LiteralPath '$($tempRoot.Replace('''', ''''''))' -Recurse -Force -ErrorAction SilentlyContinue\"");
        script.AppendLine("    Start-Process -FilePath 'powershell.exe' -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', $cleanupCommand -WindowStyle Hidden");
        script.AppendLine("    exit 0");
        script.AppendLine("} catch {");
        script.AppendLine("    Write-UpdateLog \"Update failed: $($_.Exception.Message)\"");
        script.AppendLine("    exit 1");
        script.AppendLine("}");
        return script.ToString();
    }

    private static string ToPowerShellSingleQuotedString(string value)
    {
        return $"'{value.Replace("'", "''")}'";
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
