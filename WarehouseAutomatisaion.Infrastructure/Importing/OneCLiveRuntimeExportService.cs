using System.Diagnostics;
using System.Text;

namespace WarehouseAutomatisaion.Infrastructure.Importing;

public sealed class OneCLiveRuntimeExportService
{
    private static readonly IReadOnlyList<OneCLiveExportTarget> CriticalTargets =
    [
        new("catalogs", "Контрагенты"),
        new("catalogs", "Номенклатура"),
        new("catalogs", "ДоговорыКонтрагентов"),
        new("catalogs", "Валюты"),
        new("catalogs", "ЕдиницыИзмерения"),
        new("catalogs", "ВидыЦен"),
        new("documents", "ЗаказПокупателя"),
        new("documents", "СчетНаОплату"),
        new("documents", "РасходнаяНакладная"),
        new("documents", "ЗаказПоставщику"),
        new("documents", "СчетНаОплатуПоставщика"),
        new("documents", "ПриходнаяНакладная"),
        new("documents", "ЗаказНаПеремещение"),
        new("documents", "РезервированиеЗапасов"),
        new("documents", "ИнвентаризацияЗапасов"),
        new("documents", "СписаниеЗапасов")
    ];

    private readonly OneCLiveRuntimeExportOptions _options;

    public OneCLiveRuntimeExportService(OneCLiveRuntimeExportOptions options)
    {
        _options = options;
    }

    public static OneCLiveRuntimeExportService CreateDefault()
    {
        var workspaceRoot = WorkspacePathResolver.ResolveWorkspaceRoot();
        return new OneCLiveRuntimeExportService(new OneCLiveRuntimeExportOptions
        {
            WorkspaceRoot = workspaceRoot,
            ScriptPath = Path.Combine(workspaceRoot, "scripts", "export-1c-csv.vbs"),
            OutputRoot = Path.Combine(workspaceRoot, "app_data", "one-c-live", "current"),
            UserName = Environment.GetEnvironmentVariable("WAREHOUSE_1C_USER") ?? "codex",
            Password = Environment.GetEnvironmentVariable("WAREHOUSE_1C_PASSWORD") ?? string.Empty,
            BasePath = Environment.GetEnvironmentVariable("WAREHOUSE_1C_BASE_PATH") ?? @"C:\blagodar",
            CScriptPath = ResolveCScriptPath()
        });
    }

    public async Task<OneCLiveRuntimeExportResult> ExportCriticalSnapshotAsync(CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        PrepareOutputRoot();

        var startedAtUtc = DateTime.UtcNow;
        var steps = new List<OneCLiveRuntimeStepResult>(CriticalTargets.Count);
        foreach (var target in CriticalTargets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = await RunStepAsync(target, cancellationToken);
            steps.Add(step);
            if (!step.Succeeded)
            {
                WriteCombinedLog(startedAtUtc, DateTime.UtcNow, steps);
                throw new InvalidOperationException(
                    $"Не удалось считать объект 1С \"{target.NameFilter}\".{Environment.NewLine}{Environment.NewLine}{step.GetDiagnosticText()}");
            }
        }

        var completedAtUtc = DateTime.UtcNow;
        WriteCombinedLog(startedAtUtc, completedAtUtc, steps);
        return new OneCLiveRuntimeExportResult(_options.OutputRoot, startedAtUtc, completedAtUtc, steps);
    }

    private static string ResolveCScriptPath()
    {
        var systemPath = Path.Combine(Environment.SystemDirectory, "cscript.exe");
        return File.Exists(systemPath) ? systemPath : "cscript.exe";
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.WorkspaceRoot) || !Directory.Exists(_options.WorkspaceRoot))
        {
            throw new DirectoryNotFoundException("Не найден корень workspace для live-синхронизации 1С.");
        }

        if (string.IsNullOrWhiteSpace(_options.ScriptPath) || !File.Exists(_options.ScriptPath))
        {
            throw new FileNotFoundException("Не найден скрипт live-экспорта 1С.", _options.ScriptPath);
        }

        if (string.IsNullOrWhiteSpace(_options.BasePath) || !Directory.Exists(_options.BasePath))
        {
            throw new DirectoryNotFoundException($"Не найдена файловая база 1С: {_options.BasePath}");
        }
    }

    private void PrepareOutputRoot()
    {
        if (Directory.Exists(_options.OutputRoot))
        {
            Directory.Delete(_options.OutputRoot, true);
        }

        Directory.CreateDirectory(_options.OutputRoot);
    }

    private async Task<OneCLiveRuntimeStepResult> RunStepAsync(OneCLiveExportTarget target, CancellationToken cancellationToken)
    {
        var startedAtUtc = DateTime.UtcNow;
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.CScriptPath,
            WorkingDirectory = _options.WorkspaceRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        startInfo.ArgumentList.Add("//nologo");
        startInfo.ArgumentList.Add(_options.ScriptPath);
        startInfo.ArgumentList.Add(target.BatchKind);
        startInfo.ArgumentList.Add(_options.OutputRoot);
        startInfo.ArgumentList.Add(target.NameFilter);
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add(_options.UserName);
        startInfo.ArgumentList.Add(string.IsNullOrEmpty(_options.Password) ? "__EMPTY__" : _options.Password);
        startInfo.ArgumentList.Add(_options.BasePath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException($"Не удалось запустить cscript для объекта 1С \"{target.NameFilter}\".");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(cancellationToken);

        var standardOutput = await stdoutTask;
        var standardError = await stderrTask;
        return new OneCLiveRuntimeStepResult(
            target.BatchKind,
            target.NameFilter,
            process.ExitCode,
            startedAtUtc,
            DateTime.UtcNow,
            standardOutput,
            standardError);
    }

    private void WriteCombinedLog(DateTime startedAtUtc, DateTime completedAtUtc, IReadOnlyList<OneCLiveRuntimeStepResult> steps)
    {
        var directory = Path.GetDirectoryName(_options.OutputRoot) ?? _options.WorkspaceRoot;
        Directory.CreateDirectory(directory);

        var builder = new StringBuilder();
        builder.AppendLine($"StartedUtc={startedAtUtc:O}");
        builder.AppendLine($"CompletedUtc={completedAtUtc:O}");
        builder.AppendLine($"BasePath={_options.BasePath}");
        builder.AppendLine($"OutputRoot={_options.OutputRoot}");
        builder.AppendLine($"UserName={_options.UserName}");
        builder.AppendLine();

        foreach (var step in steps)
        {
            builder.AppendLine($"[{step.BatchKind}:{step.NameFilter}] ExitCode={step.ExitCode} Duration={step.Duration}");
            if (!string.IsNullOrWhiteSpace(step.StandardOutput))
            {
                builder.AppendLine(step.StandardOutput.Trim());
            }

            if (!string.IsNullOrWhiteSpace(step.StandardError))
            {
                builder.AppendLine(step.StandardError.Trim());
            }

            builder.AppendLine();
        }

        File.WriteAllText(Path.Combine(directory, "last-sync.log"), builder.ToString(), Encoding.UTF8);
    }
}

public sealed class OneCLiveRuntimeExportOptions
{
    public string WorkspaceRoot { get; init; } = string.Empty;

    public string ScriptPath { get; init; } = string.Empty;

    public string OutputRoot { get; init; } = string.Empty;

    public string UserName { get; init; } = "codex";

    public string Password { get; init; } = string.Empty;

    public string BasePath { get; init; } = @"C:\blagodar";

    public string CScriptPath { get; init; } = "cscript.exe";
}

public sealed record OneCLiveRuntimeExportResult(
    string OutputRoot,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    IReadOnlyList<OneCLiveRuntimeStepResult> Steps)
{
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;
}

public sealed record OneCLiveRuntimeStepResult(
    string BatchKind,
    string NameFilter,
    int ExitCode,
    DateTime StartedAtUtc,
    DateTime CompletedAtUtc,
    string StandardOutput,
    string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    public string GetDiagnosticText()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Шаг: {BatchKind} / {NameFilter}");
        builder.AppendLine($"Код завершения: {ExitCode}");

        if (!string.IsNullOrWhiteSpace(StandardOutput))
        {
            builder.AppendLine();
            builder.AppendLine(StandardOutput.Trim());
        }

        if (!string.IsNullOrWhiteSpace(StandardError))
        {
            builder.AppendLine();
            builder.AppendLine(StandardError.Trim());
        }

        return builder.ToString().Trim();
    }
}

internal sealed record OneCLiveExportTarget(string BatchKind, string NameFilter);
