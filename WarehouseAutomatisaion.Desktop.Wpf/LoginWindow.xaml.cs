using System.Windows;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class LoginWindow : Window
{
    private readonly ApplicationUpdateService _applicationUpdateService = new();
    private bool _loginInProgress;
    private bool _updateInProgress;

    public DesktopClientStartupResult? StartupStatus { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        UserNameComboBox.Text = Environment.UserName;

        Loaded += HandleLoaded;
        Closed += (_, _) => _applicationUpdateService.Dispose();
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        await LoadUserOptionsAsync();

        UserNameComboBox.Focus();
        if (UserNameComboBox.Items.Count > 1)
        {
            UserNameComboBox.IsDropDownOpen = true;
        }
        else if (!string.IsNullOrWhiteSpace(UserNameComboBox.Text))
        {
            PasswordBox.Focus();
        }
    }

    private void HandleLoginClick(object sender, RoutedEventArgs e)
    {
        TryLogin();
    }

    private async void HandleUpdateClick(object sender, RoutedEventArgs e)
    {
        if (_loginInProgress || _updateInProgress)
        {
            return;
        }

        await CheckForUpdatesBeforeLoginAsync();
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        if (_updateInProgress)
        {
            return;
        }

        DialogResult = false;
    }

    private void HandleInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || _updateInProgress)
        {
            return;
        }

        e.Handled = true;
        if (ReferenceEquals(sender, UserNameComboBox) && UserNameComboBox.IsDropDownOpen)
        {
            UserNameComboBox.IsDropDownOpen = false;
            PasswordBox.Focus();
            return;
        }

        TryLogin();
    }

    private void TryLogin()
    {
        if (_updateInProgress)
        {
            return;
        }

        var userName = (UserNameComboBox.SelectedItem?.ToString() ?? UserNameComboBox.Text).Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            ShowError("Введите логин.");
            UserNameComboBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            ShowError("Введите пароль.");
            PasswordBox.Focus();
            return;
        }

        SetLoginBusy(true);
        try
        {
            var result = DesktopClientStartupService.Authenticate(userName, password);
            if (!result.CanStart)
            {
                ShowError(result.Message);
                PasswordBox.Clear();
                PasswordBox.Focus();
                return;
            }

            StartupStatus = result;
            DialogResult = true;
        }
        finally
        {
            SetLoginBusy(false);
        }
    }

    private async Task CheckForUpdatesBeforeLoginAsync()
    {
        HideError();
        SetUpdateBusy(true, "Проверяю...");

        try
        {
            var result = await _applicationUpdateService.CheckForUpdatesAsync();
            if (result.State != ApplicationUpdateCheckState.UpdateAvailable || result.Release is null)
            {
                var icon = result.State == ApplicationUpdateCheckState.Failed
                    ? MessageBoxImage.Warning
                    : MessageBoxImage.Information;
                MessageBox.Show(this, result.Message, AppBranding.MessageBoxTitle, MessageBoxButton.OK, icon);
                return;
            }

            var confirmation = MessageBox.Show(
                this,
                $"Доступна версия {result.Release.Version}. Установить обновление сейчас?{Environment.NewLine}{Environment.NewLine}Приложение закроется и запустится заново после установки.",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
            {
                return;
            }

            SetUpdateBusy(true, "Скачиваю...");
            var launchResult = await _applicationUpdateService.PrepareAndLaunchUpdateAsync(result.Release);
            if (!launchResult.IsSuccess)
            {
                MessageBox.Show(this, launchResult.Message, AppBranding.MessageBoxTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            MessageBox.Show(this, launchResult.Message, AppBranding.MessageBoxTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            System.Windows.Application.Current.Shutdown(0);
        }
        catch (Exception exception)
        {
            ShowError($"Не удалось проверить обновление.{Environment.NewLine}{Environment.NewLine}{exception.Message}");
        }
        finally
        {
            if (IsVisible)
            {
                SetUpdateBusy(false, "Обновить");
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void HideError()
    {
        ErrorText.Text = string.Empty;
        ErrorBorder.Visibility = Visibility.Collapsed;
    }

    private void SetLoginBusy(bool isBusy)
    {
        _loginInProgress = isBusy;
        LoginButton.IsEnabled = !isBusy && !_updateInProgress;
        LoginUpdateButton.IsEnabled = !isBusy && !_updateInProgress;
        UserNameComboBox.IsEnabled = !isBusy && !_updateInProgress;
        PasswordBox.IsEnabled = !isBusy && !_updateInProgress;
        CancelButton.IsEnabled = !isBusy && !_updateInProgress;
        LoginButton.Content = isBusy ? "Проверяю..." : "Войти";
    }

    private void SetUpdateBusy(bool isBusy, string buttonText)
    {
        _updateInProgress = isBusy;
        LoginUpdateButton.Content = buttonText;
        LoginUpdateButton.IsEnabled = !isBusy && !_loginInProgress;
        LoginButton.IsEnabled = !isBusy && !_loginInProgress;
        UserNameComboBox.IsEnabled = !isBusy && !_loginInProgress;
        PasswordBox.IsEnabled = !isBusy && !_loginInProgress;
        CancelButton.IsEnabled = !isBusy && !_loginInProgress;
    }

    private async Task LoadUserOptionsAsync()
    {
        try
        {
            var users = await Task.Run(() =>
            {
                var backplane = DesktopMySqlBackplaneService.TryCreateDefault();
                if (backplane is null)
                {
                    return Array.Empty<string>();
                }

                return backplane.ListUserAccounts()
                    .Where(item => item.IsActive && item.HasPassword)
                    .Select(item => item.UserName)
                    .Where(item => !string.IsNullOrWhiteSpace(item))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(item => item, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            });

            if (users.Length == 0)
            {
                return;
            }

            UserNameComboBox.ItemsSource = users;
            if (string.IsNullOrWhiteSpace(UserNameComboBox.Text)
                || !users.Contains(UserNameComboBox.Text, StringComparer.OrdinalIgnoreCase))
            {
                UserNameComboBox.Text = users[0];
            }
        }
        catch
        {
        }
    }
}
