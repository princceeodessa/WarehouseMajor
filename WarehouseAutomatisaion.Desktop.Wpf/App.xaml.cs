using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WpfMouseWheelScrollFix.Register();
        RegisterGlobalExceptionHandlers();

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
            WriteClientErrorLog(exception, "App.OnStartup");
            System.Windows.MessageBox.Show(
                $"Не удалось запустить WPF-клиент.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    internal static void WriteClientErrorLog(Exception exception, string context)
    {
        try
        {
            var root = ResolveWorkspaceRoot();
            var logDirectory = Path.Combine(root, "app_data");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "desktop-client-error.log");
            var entry = string.Join(
                Environment.NewLine,
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {context}",
                exception.ToString(),
                new string('-', 96),
                string.Empty);
            File.AppendAllText(logPath, entry, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void RegisterGlobalExceptionHandlers()
    {
        Current.DispatcherUnhandledException += HandleDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                WriteClientErrorLog(exception, "AppDomain.UnhandledException");
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            WriteClientErrorLog(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }

    private static void HandleDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        WriteClientErrorLog(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
        System.Windows.MessageBox.Show(
            $"Приложение перехватило ошибку и продолжит работу.{Environment.NewLine}{Environment.NewLine}{e.Exception.Message}",
            AppBranding.MessageBoxTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string ResolveWorkspaceRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WarehouseAutomatisaion.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return AppContext.BaseDirectory;
    }
}
