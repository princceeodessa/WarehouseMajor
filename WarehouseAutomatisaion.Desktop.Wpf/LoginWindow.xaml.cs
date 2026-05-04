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
        UserNameTextBox.Text = Environment.UserName;

        Loaded += (_, _) =>
        {
            UserNameTextBox.Focus();
            UserNameTextBox.SelectAll();
        };
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
        TryLogin();
    }

    private void TryLogin()
    {
        var userName = UserNameTextBox.Text.Trim();
        var password = PasswordBox.Password;

        if (string.IsNullOrWhiteSpace(userName))
        {
            ShowError("Введите логин.");
            UserNameTextBox.Focus();
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
        UserNameTextBox.IsEnabled = !isBusy;
        PasswordBox.IsEnabled = !isBusy;
        LoginButton.Content = isBusy ? "Проверяю..." : "Войти";
    }
}
