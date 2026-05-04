using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SystemSettingsWorkspaceView : UserControl
{
    private static readonly IReadOnlyList<SettingsRoleOption> RoleOptions =
    [
        new("manager", "Менеджер"),
        new("admin", "Администратор")
    ];

    private readonly DesktopClientStartupResult _startupStatus;
    private readonly ApplicationUpdateOptions _updateOptions;
    private readonly DesktopMySqlBackplaneService? _backplane;
    private readonly string _appSettingsPath;
    private readonly string _localSettingsPath;
    private bool _isUserFormBusy;

    public SystemSettingsWorkspaceView(
        DesktopClientStartupResult startupStatus,
        RecordsWorkspaceDefinition dataLinksDefinition)
    {
        _startupStatus = startupStatus;
        _updateOptions = ApplicationUpdateSettings.Snapshot();
        _backplane = startupStatus.UsesSharedDatabase
            ? DesktopMySqlBackplaneService.TryCreateDefault()
            : null;
        _appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        _localSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.local.json");

        InitializeComponent();
        DataLinksHost.Content = new RecordsWorkspaceView(dataLinksDefinition);
        UserRoleComboBox.ItemsSource = RoleOptions;
        UserRoleComboBox.SelectedValue = "manager";
        PopulateSettings();
        RefreshUsers();
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

    private void HandleRefreshUsersClick(object sender, RoutedEventArgs e)
    {
        RefreshUsers();
    }

    private void HandleUsersSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersDataGrid.SelectedItem is not SettingsUserRowViewModel user)
        {
            UpdateUserActionButtons();
            return;
        }

        UserLoginTextBox.Text = user.UserName;
        UserDisplayNameTextBox.Text = user.DisplayName;
        UserRoleComboBox.SelectedValue = string.IsNullOrWhiteSpace(user.RoleCode) ? "manager" : user.RoleCode;
        UserPasswordBox.Clear();
        UserEditorTitleText.Text = "Пользователь";
        SaveUserButton.Content = "Обновить пользователя";
        UserFormStatusText.Text = "Для обновления пользователя укажите новый пароль.";
        UpdateUserActionButtons();
    }

    private void HandleClearUserFormClick(object sender, RoutedEventArgs e)
    {
        ClearUserForm();
    }

    private void HandleUserPasswordChanged(object sender, RoutedEventArgs e)
    {
        UpdateUserActionButtons();
    }

    private void HandleSaveUserClick(object sender, RoutedEventArgs e)
    {
        if (_backplane is null)
        {
            UserFormStatusText.Text = "Создание пользователей доступно только при подключении к общей базе.";
            return;
        }

        var userName = UserLoginTextBox.Text.Trim();
        var displayName = UserDisplayNameTextBox.Text.Trim();
        var roleCode = UserRoleComboBox.SelectedValue as string ?? "manager";
        var password = UserPasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            UserFormStatusText.Text = "Введите логин пользователя.";
            UserLoginTextBox.Focus();
            return;
        }

        if (userName.Any(char.IsWhiteSpace))
        {
            UserFormStatusText.Text = "Логин не должен содержать пробелы.";
            UserLoginTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            UserFormStatusText.Text = "Введите пароль не короче 6 символов.";
            UserPasswordBox.Focus();
            return;
        }

        SetUserFormBusy(true);
        try
        {
            _backplane.UpsertUserWithPassword(
                userName,
                string.IsNullOrWhiteSpace(displayName) ? userName : displayName,
                roleCode,
                password,
                _startupStatus.UserName);
            UserPasswordBox.Clear();
            RefreshUsers(selectUserName: userName);
            SaveUserButton.Content = "Обновить пользователя";
            UserFormStatusText.Text = $"Пользователь {userName} сохранен.";
        }
        catch (Exception exception)
        {
            UserFormStatusText.Text = $"Не удалось сохранить пользователя: {exception.Message}";
        }
        finally
        {
            SetUserFormBusy(false);
        }
    }

    private void HandleChangePasswordClick(object sender, RoutedEventArgs e)
    {
        if (_backplane is null)
        {
            UserFormStatusText.Text = "Управление пользователями доступно только при подключении к общей базе.";
            return;
        }

        if (SelectedUser is not { } user)
        {
            UserFormStatusText.Text = "Выберите пользователя в таблице.";
            return;
        }

        var password = UserPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
        {
            UserFormStatusText.Text = "Введите новый пароль не короче 6 символов.";
            UserPasswordBox.Focus();
            return;
        }

        var displayName = string.IsNullOrWhiteSpace(UserDisplayNameTextBox.Text)
            ? user.DisplayName
            : UserDisplayNameTextBox.Text.Trim();
        var roleCode = UserRoleComboBox.SelectedValue as string;
        if (string.IsNullOrWhiteSpace(roleCode))
        {
            roleCode = string.IsNullOrWhiteSpace(user.RoleCode) ? "manager" : user.RoleCode;
        }

        SetUserFormBusy(true);
        try
        {
            _backplane.UpsertUserWithPassword(user.UserName, displayName, roleCode, password, _startupStatus.UserName);
            UserPasswordBox.Clear();
            RefreshUsers(selectUserName: user.UserName);
            UserFormStatusText.Text = $"Пароль пользователя {user.UserName} обновлен.";
        }
        catch (Exception exception)
        {
            UserFormStatusText.Text = $"Не удалось сменить пароль: {exception.Message}";
        }
        finally
        {
            SetUserFormBusy(false);
        }
    }

    private void HandleDisableUserClick(object sender, RoutedEventArgs e)
    {
        SetSelectedUserActive(isActive: false);
    }

    private void HandleEnableUserClick(object sender, RoutedEventArgs e)
    {
        SetSelectedUserActive(isActive: true);
    }

    private void HandleResetLoginFailuresClick(object sender, RoutedEventArgs e)
    {
        if (_backplane is null)
        {
            UserFormStatusText.Text = "Управление пользователями доступно только при подключении к общей базе.";
            return;
        }

        if (SelectedUser is not { } user)
        {
            UserFormStatusText.Text = "Выберите пользователя в таблице.";
            return;
        }

        SetUserFormBusy(true);
        try
        {
            _backplane.ResetUserLoginFailures(user.UserName, _startupStatus.UserName);
            RefreshUsers(selectUserName: user.UserName);
            UserFormStatusText.Text = $"Счетчик ошибок входа для {user.UserName} сброшен.";
        }
        catch (Exception exception)
        {
            UserFormStatusText.Text = $"Не удалось сбросить ошибки входа: {exception.Message}";
        }
        finally
        {
            SetUserFormBusy(false);
        }
    }

    private void SetSelectedUserActive(bool isActive)
    {
        if (_backplane is null)
        {
            UserFormStatusText.Text = "Управление пользователями доступно только при подключении к общей базе.";
            return;
        }

        if (SelectedUser is not { } user)
        {
            UserFormStatusText.Text = "Выберите пользователя в таблице.";
            return;
        }

        if (!isActive && IsCurrentUser(user))
        {
            UserFormStatusText.Text = "Нельзя отключить текущего пользователя.";
            return;
        }

        SetUserFormBusy(true);
        try
        {
            _backplane.SetUserActive(user.UserName, isActive, _startupStatus.UserName);
            RefreshUsers(selectUserName: user.UserName);
            UserFormStatusText.Text = isActive
                ? $"Пользователь {user.UserName} включен."
                : $"Пользователь {user.UserName} отключен.";
        }
        catch (Exception exception)
        {
            UserFormStatusText.Text = isActive
                ? $"Не удалось включить пользователя: {exception.Message}"
                : $"Не удалось отключить пользователя: {exception.Message}";
        }
        finally
        {
            SetUserFormBusy(false);
        }
    }

    private void RefreshUsers(string? selectUserName = null)
    {
        if (_backplane is null)
        {
            UsersDataGrid.ItemsSource = Array.Empty<SettingsUserRowViewModel>();
            UsersSummaryText.Text = "Общая база отключена. Пользователи хранятся на сервере.";
            UserFormStatusText.Text = "Создание пользователей доступно только при подключении к общей базе.";
            SetUserFormEnabled(false);
            UpdateUserActionButtons();
            return;
        }

        try
        {
            var users = _backplane.ListUserAccounts()
                .Select(SettingsUserRowViewModel.FromAccount)
                .ToArray();
            UsersDataGrid.ItemsSource = users;
            UsersSummaryText.Text = $"Всего пользователей: {users.Length:N0}. Администраторов: {users.Count(item => item.RoleCode == "admin"):N0}.";
            SetUserFormEnabled(true);

            if (!string.IsNullOrWhiteSpace(selectUserName))
            {
                UsersDataGrid.SelectedItem = users.FirstOrDefault(item => item.UserName.Equals(selectUserName, StringComparison.OrdinalIgnoreCase));
            }

            UpdateUserActionButtons();
        }
        catch (Exception exception)
        {
            UsersDataGrid.ItemsSource = Array.Empty<SettingsUserRowViewModel>();
            UsersSummaryText.Text = "Не удалось загрузить пользователей.";
            UserFormStatusText.Text = $"Ошибка загрузки пользователей: {exception.Message}";
            SetUserFormEnabled(false);
            UpdateUserActionButtons();
        }
    }

    private void ClearUserForm()
    {
        UsersDataGrid.SelectedItem = null;
        UserLoginTextBox.Clear();
        UserDisplayNameTextBox.Clear();
        UserPasswordBox.Clear();
        UserRoleComboBox.SelectedValue = "manager";
        UserEditorTitleText.Text = "Новый пользователь";
        SaveUserButton.Content = "Создать пользователя";
        UserFormStatusText.Text = string.Empty;
        UpdateUserActionButtons();
        UserLoginTextBox.Focus();
    }

    private void SetUserFormBusy(bool isBusy)
    {
        _isUserFormBusy = isBusy;
        SaveUserButton.IsEnabled = !isBusy;
        UserLoginTextBox.IsEnabled = !isBusy;
        UserDisplayNameTextBox.IsEnabled = !isBusy;
        UserRoleComboBox.IsEnabled = !isBusy;
        UserPasswordBox.IsEnabled = !isBusy;
        SaveUserButton.Content = isBusy
            ? "Сохраняю..."
            : UsersDataGrid.SelectedItem is null ? "Создать пользователя" : "Обновить пользователя";
        UpdateUserActionButtons();
    }

    private void SetUserFormEnabled(bool isEnabled)
    {
        SaveUserButton.IsEnabled = isEnabled;
        UserLoginTextBox.IsEnabled = isEnabled;
        UserDisplayNameTextBox.IsEnabled = isEnabled;
        UserRoleComboBox.IsEnabled = isEnabled;
        UserPasswordBox.IsEnabled = isEnabled;
        UpdateUserActionButtons();
    }

    private SettingsUserRowViewModel? SelectedUser => UsersDataGrid.SelectedItem as SettingsUserRowViewModel;

    private void UpdateUserActionButtons()
    {
        var user = SelectedUser;
        var hasSelectedUser = _backplane is not null && !_isUserFormBusy && user is not null;
        var hasNewPassword = UserPasswordBox.Password.Length >= 6;

        ChangePasswordButton.IsEnabled = hasSelectedUser && hasNewPassword;
        ResetLoginFailuresButton.IsEnabled = hasSelectedUser && user!.FailedLoginCount > 0;
        DisableUserButton.IsEnabled = hasSelectedUser && user!.IsActive && !IsCurrentUser(user);
        EnableUserButton.IsEnabled = hasSelectedUser && !user!.IsActive;
    }

    private bool IsCurrentUser(SettingsUserRowViewModel user)
    {
        return user.UserName.Equals(_startupStatus.UserName, StringComparison.OrdinalIgnoreCase);
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

public sealed record SettingsRoleOption(
    string RoleCode,
    string DisplayName);

public sealed class SettingsUserRowViewModel
{
    private SettingsUserRowViewModel(DesktopAppUserAccount account)
    {
        UserName = account.UserName;
        DisplayName = account.DisplayName;
        IsActive = account.IsActive;
        RoleCode = account.RoleCode;
        RoleDisplayName = account.RoleDisplayName;
        StatusText = account.IsActive ? "Активен" : "Отключен";
        PasswordStateText = account.HasPassword ? "Задан" : "Нет";
        LastLoginText = string.IsNullOrWhiteSpace(account.LastLoginAtUtc)
            ? "Еще не входил"
            : account.LastLoginAtUtc;
        FailedLoginCount = account.FailedLoginCount;
    }

    public string UserName { get; }

    public string DisplayName { get; }

    public bool IsActive { get; }

    public string RoleCode { get; }

    public string RoleDisplayName { get; }

    public string StatusText { get; }

    public string PasswordStateText { get; }

    public string LastLoginText { get; }

    public int FailedLoginCount { get; }

    public static SettingsUserRowViewModel FromAccount(DesktopAppUserAccount account)
    {
        return new SettingsUserRowViewModel(account);
    }
}
