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

            var startupStatus = DesktopClientStartupService.Validate(Environment.UserName);
            if (!startupStatus.CanStart)
            {
                System.Windows.MessageBox.Show(
                    startupStatus.Message,
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Shutdown(-2);
                return;
            }

            var window = new MainWindow();
            MainWindow = window;
            window.Show();
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
