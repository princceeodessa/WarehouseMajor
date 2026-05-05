using System.Windows;
using System.Windows.Input;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class LoginWindow : Window
{
    public DesktopClientStartupResult? StartupStatus { get; private set; }

    public LoginWindow()
    {
        InitializeComponent();
        UserNameComboBox.Text = Environment.UserName;

        Loaded += HandleLoaded;
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

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void HandleInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
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

        SetBusy(true);
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
            SetBusy(false);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorBorder.Visibility = Visibility.Visible;
    }

    private void SetBusy(bool isBusy)
    {
        LoginButton.IsEnabled = !isBusy;
        UserNameComboBox.IsEnabled = !isBusy;
        PasswordBox.IsEnabled = !isBusy;
        LoginButton.Content = isBusy ? "Проверяю..." : "Войти";
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
