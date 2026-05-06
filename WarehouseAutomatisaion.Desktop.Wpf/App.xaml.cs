using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        WpfMouseWheelScrollFix.Register();
        RegisterGlobalExceptionHandlers();

        StartupLoadingWindow? loadingWindow = null;
        try
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var validateStopwatch = Stopwatch.StartNew();
            var infrastructureStatus = DesktopClientStartupService.ValidateInfrastructure();
            validateStopwatch.Stop();
            WriteStartupPerformanceLog("ValidateInfrastructure", validateStopwatch.Elapsed, infrastructureStatus);
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
            loadingWindow = new StartupLoadingWindow(startupStatus);
            loadingWindow.Show();
            loadingWindow.SetStatus("Читаю рабочую область из общей базы...");

            var workspaceStopwatch = Stopwatch.StartNew();
            var startupData = await WarehouseAutomatisaion.Desktop.Wpf.MainWindow.LoadStartupDataAsync(startupStatus);
            workspaceStopwatch.Stop();
            WriteStartupPerformanceLog("LoadMainWorkspace", workspaceStopwatch.Elapsed, startupStatus);

            loadingWindow.SetStatus("Открываю интерфейс...");
            var window = new MainWindow(startupStatus, startupData);
            MainWindow = window;
            window.Show();
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            loadingWindow.Close();
            loadingWindow = null;
        }
        catch (Exception exception)
        {
            loadingWindow?.Close();
            WriteClientErrorLog(exception, "App.OnStartup");
            System.Windows.MessageBox.Show(
                $"Не удалось запустить WPF-клиент.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    private static void WriteStartupPerformanceLog(
        string stage,
        TimeSpan elapsed,
        DesktopClientStartupResult? startupStatus)
    {
        try
        {
            var root = ResolveWorkspaceRoot();
            var logDirectory = Path.Combine(root, "app_data");
            Directory.CreateDirectory(logDirectory);
            var logPath = Path.Combine(logDirectory, "desktop-startup-performance.log");
            var mode = startupStatus?.UsesSharedDatabase == true ? "shared-db" : "local";
            var entry =
                $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}; version={AppBranding.CurrentVersion}; stage={stage}; elapsed={elapsed.TotalMilliseconds:N0}ms; mode={mode}; canStart={startupStatus?.CanStart.ToString() ?? "n/a"}{Environment.NewLine}";
            File.AppendAllText(logPath, entry, Encoding.UTF8);
        }
        catch
        {
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
