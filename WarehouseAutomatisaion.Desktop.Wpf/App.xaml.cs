using System.Text;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WpfMouseWheelScrollFix.Register();

        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var infrastructureStatus = DesktopClientStartupService.ValidateInfrastructure();
            if (!infrastructureStatus.CanStart)
            {
                System.Windows.MessageBox.Show(
                    infrastructureStatus.Message,
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-2);
                return;
            }

            var loginWindow = new LoginWindow();
            var loginResult = loginWindow.ShowDialog();
            if (loginResult != true || loginWindow.StartupStatus is null)
            {
                Shutdown(0);
                return;
            }

            var startupStatus = loginWindow.StartupStatus;
            var window = new MainWindow(startupStatus);
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(
                $"Не удалось запустить WPF-клиент.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
