using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using WarehouseAutomatisaion.Infrastructure.Options;

namespace WarehouseAutomatisaion.Infrastructure.Ai;

// Sprint 14: общая добыча X-Sync-Token для всех обращений к 1C/Ollama API.
// Раньше логика жила внутри OneCSyncAssistantChatService; вынесена, чтобы
// её переиспользовали и чат, и аналитические инструменты (один кэш на сессию).
//
// Источники по приоритету: options.SyncToken -> env CHAT_SYNC_TOKEN/SYNC_TOKEN
// -> (если разрешено) чтение /opt/onec-sync/shared/.env по SSH-ключу.
public sealed class OneCSyncTokenResolver
{
    private readonly OneCSyncAssistantOptions _options;
    private readonly ILogger _logger;
    private string? _cachedToken;

    public OneCSyncTokenResolver(OneCSyncAssistantOptions options, ILogger logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string?> ResolveAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedToken))
        {
            return _cachedToken;
        }

        var token = FirstNotBlank(
            _options.SyncToken,
            Environment.GetEnvironmentVariable("CHAT_SYNC_TOKEN"),
            Environment.GetEnvironmentVariable("SYNC_TOKEN"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token.Trim();
            return _cachedToken;
        }

        if (!_options.FetchTokenViaSsh)
        {
            return null;
        }

        token = await FetchTokenViaSshAsync(cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(token))
        {
            _cachedToken = token.Trim();
        }

        return _cachedToken;
    }

    private async Task<string?> FetchTokenViaSshAsync(CancellationToken cancellationToken)
    {
        var sshExe = FindSshExe();
        var keyPath = ResolveExistingPath(_options.SshKeyPath);
        var knownHostsPath = ResolveExistingPath(_options.SshKnownHostsPath);

        if (string.IsNullOrWhiteSpace(sshExe)
            || string.IsNullOrWhiteSpace(keyPath)
            || string.IsNullOrWhiteSpace(_options.SshHost))
        {
            return null;
        }

        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = sshExe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        process.StartInfo.ArgumentList.Add("-i");
        process.StartInfo.ArgumentList.Add(keyPath);
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add("BatchMode=yes");
        process.StartInfo.ArgumentList.Add("-o");
        process.StartInfo.ArgumentList.Add("IdentitiesOnly=yes");
        if (!string.IsNullOrWhiteSpace(knownHostsPath))
        {
            process.StartInfo.ArgumentList.Add("-o");
            process.StartInfo.ArgumentList.Add($"UserKnownHostsFile={knownHostsPath}");
        }
        process.StartInfo.ArgumentList.Add(_options.SshHost);
        process.StartInfo.ArgumentList.Add("grep '^SYNC_TOKEN=' /opt/onec-sync/shared/.env | cut -d= -f2-");

        try
        {
            process.Start();
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var exited = await WaitForExitAsync(process, TimeSpan.FromSeconds(20), cancellationToken)
                .ConfigureAwait(false);

            if (!exited)
            {
                TryKill(process);
                return null;
            }

            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                _logger.LogWarning("SSH token fetch failed with exit code {ExitCode}: {Error}", process.ExitCode, error);
                return null;
            }

            return output.Trim();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Failed to fetch 1C/Ollama API token via SSH");
            return null;
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var timeoutTask = Task.Delay(timeout, cancellationToken);
        return await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false) == waitTask;
    }

    private static string? FirstNotBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string? FindSshExe()
    {
        var systemSsh = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32",
            "OpenSSH",
            "ssh.exe");

        if (File.Exists(systemSsh))
        {
            return systemSsh;
        }

        return "ssh";
    }

    private static string? ResolveExistingPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var expanded = Environment.ExpandEnvironmentVariables(path);
        if (Path.IsPathRooted(expanded))
        {
            return File.Exists(expanded) ? expanded : null;
        }

        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, expanded),
            Path.Combine(AppContext.BaseDirectory, expanded),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", expanded))
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }
}
