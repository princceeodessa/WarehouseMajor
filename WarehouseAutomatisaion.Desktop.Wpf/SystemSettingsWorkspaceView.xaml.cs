using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SystemSettingsWorkspaceView : UserControl
{
    private readonly DesktopClientStartupResult _startupStatus;
    private readonly ApplicationUpdateOptions _updateOptions;
    private readonly string _appSettingsPath;
    private readonly string _localSettingsPath;

    public SystemSettingsWorkspaceView(
        DesktopClientStartupResult startupStatus,
        RecordsWorkspaceDefinition dataLinksDefinition)
    {
        _startupStatus = startupStatus;
        _updateOptions = ApplicationUpdateSettings.Snapshot();
        _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _localSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

        InitializeComponent();
        DataLinksHost.Content = new RecordsWorkspaceView(dataLinksDefinition);
        PopulateSettings();
    }

    private void PopulateSettings()
    {
        ProductNameText.Text = AppBranding.ProductName;
        VersionText.Text = AppBranding.CurrentVersion;
        ExecutableNameText.Text = AppBranding.ExecutableName;
        CurrentUserText.Text = Clean(_startupStatus.DisplayName, _startupStatus.UserName, Environment.UserName);
        CurrentRoleText.Text = Clean(_startupStatus.PrimaryRoleDisplayName);
        AccessLevelText.Text = _startupStatus.PrimaryRoleCode.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? "Полный доступ к настройкам"
            : "Рабочий доступ менеджера";
        DataModeText.Text = _startupStatus.UsesSharedDatabase ? "Общая серверная база" : "Локальные данные";
        ApplicationFolderText.Text = AppContext.BaseDirectory;

        DatabaseStateText.Text = _startupStatus.UsesSharedDatabase ? "Подключено" : "Локальный режим";
        DatabaseEndpointText.Text = _startupStatus.UsesSharedDatabase
            ? $"{_startupStatus.Host}:{_startupStatus.Port}"
            : "Не используется";
        DatabaseNameText.Text = Clean(_startupStatus.Database, "Не используется");
        DatabaseUserText.Text = Clean(_startupStatus.User, "Не используется");
        AuthenticationModeText.Text = _startupStatus.UsesSharedDatabase
            ? "Логин и пароль проверяются на сервере"
            : "Серверная проверка отключена";

        UpdateStateText.Text = _updateOptions.IsConfigured ? "Включено" : "Отключено или не настроено";
        UpdateRepositoryText.Text = string.IsNullOrWhiteSpace(_updateOptions.GitHubOwner) || string.IsNullOrWhiteSpace(_updateOptions.GitHubRepository)
            ? "Не задан"
            : $"{_updateOptions.GitHubOwner}/{_updateOptions.GitHubRepository}";
        UpdateAssetText.Text = Clean(_updateOptions.AssetName, "Не задан");
        AppSettingsPathText.Text = _appSettingsPath;
        LocalSettingsPathText.Text = File.Exists(_localSettingsPath)
            ? _localSettingsPath
            : $"{_localSettingsPath} не создан";

        SummaryCardsItemsControl.ItemsSource = BuildSummaryCards();
    }

    private IReadOnlyList<SettingsSummaryCard> BuildSummaryCards()
    {
        return
        [
            new(
                "Версия",
                AppBranding.CurrentVersion,
                AppBranding.ProductName,
                BrushFromHex("#4F5BFF"),
                BrushFromHex("#EEF2FF"),
                "\uE946"),
            new(
                "Пользователь",
                Clean(_startupStatus.DisplayName, _startupStatus.UserName, Environment.UserName),
                Clean(_startupStatus.PrimaryRoleDisplayName),
                BrushFromHex("#1F8F50"),
                BrushFromHex("#EAF8F0"),
                "\uE77B"),
            new(
                "База данных",
                _startupStatus.UsesSharedDatabase ? "Общая" : "Локальная",
                _startupStatus.UsesSharedDatabase ? Clean(_startupStatus.Database) : "без сервера",
                BrushFromHex("#4F8CFF"),
                BrushFromHex("#EEF4FF"),
                "\uE8D4"),
            new(
                "Обновления",
                _updateOptions.IsConfigured ? "Включены" : "Отключены",
                Clean(_updateOptions.AssetName, "канал не задан"),
                BrushFromHex("#F29A17"),
                BrushFromHex("#FFF4E3"),
                "\uE895")
        ];
    }

    private void HandleOpenAppFolderClick(object sender, RoutedEventArgs e)
    {
        OpenFolder(AppContext.BaseDirectory);
    }

    private void HandleOpenLocalSettingsClick(object sender, RoutedEventArgs e)
    {
        if (File.Exists(_localSettingsPath))
        {
            SelectFile(_localSettingsPath);
            return;
        }

        OpenFolder(AppContext.BaseDirectory);
    }

    private void HandleCopyDatabaseSettingsClick(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(string.Join(Environment.NewLine,
        [
            $"Режим: {(_startupStatus.UsesSharedDatabase ? "общая база" : "локальные данные")}",
            $"Сервер: {(_startupStatus.UsesSharedDatabase ? $"{_startupStatus.Host}:{_startupStatus.Port}" : "не используется")}",
            $"База: {Clean(_startupStatus.Database, "не используется")}",
            $"Пользователь подключения: {Clean(_startupStatus.User, "не используется")}"
        ]));
        ActionStatusText.Text = "Сведения о подключении скопированы.";
    }

    private void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            ActionStatusText.Text = "Папка не найдена.";
            return;
        }

        StartExplorer($"\"{path}\"");
        ActionStatusText.Text = "Папка открыта.";
    }

    private void SelectFile(string path)
    {
        if (!File.Exists(path))
        {
            ActionStatusText.Text = "Файл не найден.";
            return;
        }

        StartExplorer($"/select,\"{path}\"");
        ActionStatusText.Text = "Файл настроек открыт в проводнике.";
    }

    private static void StartExplorer(string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static string Clean(params string?[] values)
    {
        return values.FirstOrDefault(item => !string.IsNullOrWhiteSpace(item))?.Trim() ?? "-";
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        return (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
    }
}

public sealed record SettingsSummaryCard(
    string Title,
    string Value,
    string Hint,
    Brush Accent,
    Brush IconBackground,
    string IconGlyph);
