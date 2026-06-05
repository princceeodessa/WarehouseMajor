using System.Text.Json;

namespace WarehouseAutomatisaion.Desktop.Data;

// Sprint 13 fix: централизованный поиск appsettings.local.json для AI-сервисов.
// Раньше каждая factory искала только в AppContext.BaseDirectory — это работает
// в dev-сборке, но в release-сборке через auto-update файл часто отсутствует
// (он gitignored, не публикуется через GitHub Actions).
//
// Теперь ищем по нескольким стандартным путям и возвращаем первый найденный.
// Также предоставляем готовый путь куда пользователь должен положить файл
// если ничего не найдено — для UI подсказки.
public static class AppConfigLocator
{
    private const string LocalConfigFileName = "appsettings.local.json";

    /// <summary>Папка под user-edit config. Создаётся при первом запросе.</summary>
    public static string UserConfigDirectory
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Major");
            try { Directory.CreateDirectory(dir); } catch { /* best effort */ }
            return dir;
        }
    }

    /// <summary>Рекомендуемый путь к appsettings.local.json в user-data (%LOCALAPPDATA%\Major\).
    /// Этот путь показываем в UI оператору когда он спрашивает «куда положить ключи».</summary>
    public static string UserConfigFilePath => Path.Combine(UserConfigDirectory, LocalConfigFileName);

    /// <summary>Полный список путей где ищем appsettings.local.json в порядке приоритета.</summary>
    public static IReadOnlyList<string> CandidatePaths => new[]
    {
        // 1. Папка с exe (классическое поведение).
        Path.Combine(AppContext.BaseDirectory, LocalConfigFileName),
        // 2. User-edit override в %LOCALAPPDATA%\Major\.
        UserConfigFilePath,
        // 3. Dev-source: подпапка WPF проекта (используется при dotnet run).
        Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "WarehouseAutomatisaion.Desktop.Wpf", LocalConfigFileName)),
    };

    /// <summary>Найти первый существующий файл config. Null если ничего нет.</summary>
    public static string? FindLocalConfigPath()
    {
        foreach (var path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }
        return null;
    }

    /// <summary>Прочитать AiProviders секцию из любого найденного файла.
    /// Возвращает (json-text-of-section, source-path) или null если не найдено.</summary>
    public static (string SectionJson, string SourcePath)? TryReadAiProvidersSection()
    {
        return TryReadSection("AiProviders");
    }

    /// <summary>Прочитать произвольную верхнеуровневую секцию из найденного appsettings.local.json.</summary>
    public static (string SectionJson, string SourcePath)? TryReadSection(string sectionName)
    {
        foreach (var path in CandidatePaths)
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                if (document.RootElement.TryGetProperty(sectionName, out var section))
                {
                    return (section.GetRawText(), path);
                }
            }
            catch
            {
                // Try next candidate.
            }
        }
        return null;
    }
}
