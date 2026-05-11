using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan MainWorkspaceLoadTimeout = TimeSpan.FromSeconds(10);

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
            RegisterWorkspaceWindowBehavior();

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
            var startupDataTask = WarehouseAutomatisaion.Desktop.Wpf.MainWindow.LoadStartupDataAsync(startupStatus);
            var completedTask = await Task.WhenAny(startupDataTask, Task.Delay(MainWorkspaceLoadTimeout));
            MainWindowStartupData startupData;
            if (completedTask == startupDataTask)
            {
                startupData = await startupDataTask;
            }
            else
            {
                _ = startupDataTask.ContinueWith(
                    task => WriteClientErrorLog(task.Exception!, "App.LoadMainWorkspaceBackground"),
                    TaskContinuationOptions.OnlyOnFaulted);
                loadingWindow.SetStatus("Открываю интерфейс. Данные подтянутся в фоне...");
                startupData = WarehouseAutomatisaion.Desktop.Wpf.MainWindow.CreateDeferredStartupData(startupStatus);
            }

            workspaceStopwatch.Stop();
            WriteStartupPerformanceLog(
                completedTask == startupDataTask ? "LoadMainWorkspace" : "LoadMainWorkspaceTimeout",
                workspaceStopwatch.Elapsed,
                startupStatus);

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

    private static void RegisterWorkspaceWindowBehavior()
    {
        EventManager.RegisterClassHandler(
            typeof(Window),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(HandleWorkspaceWindowLoaded),
            handledEventsToo: true);
        EventManager.RegisterClassHandler(
            typeof(Window),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(HandleWorkspaceWindowPreviewKeyDown),
            handledEventsToo: true);
    }

    private static void HandleWorkspaceWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window || !IsWorkspaceChildWindow(window))
        {
            return;
        }

        EnsureWorkspaceWindowFitsScreen(window);
        EnsureWorkspaceWindowContentScrollable(window);

        if (window.ResizeMode == ResizeMode.NoResize || window.ResizeMode == ResizeMode.CanMinimize)
        {
            window.ResizeMode = ResizeMode.CanResizeWithGrip;
        }

        window.StateChanged -= HandleWorkspaceWindowStateChanged;
        window.StateChanged += HandleWorkspaceWindowStateChanged;
        window.ShowInTaskbar = true;
        window.Dispatcher.BeginInvoke(() => MoveWorkspaceWindowIntoWorkArea(window), DispatcherPriority.ApplicationIdle);
        window.Dispatcher.BeginInvoke(EnableVisibleApplicationWindows, DispatcherPriority.ApplicationIdle);
    }

    private static void EnsureWorkspaceWindowFitsScreen(Window window)
    {
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(640d, workArea.Width - 32d);
        var maxHeight = Math.Max(420d, workArea.Height - 32d);

        window.MaxWidth = LimitMaximum(window.MaxWidth, maxWidth);
        window.MaxHeight = LimitMaximum(window.MaxHeight, maxHeight);

        if (window.MinWidth > maxWidth)
        {
            window.MinWidth = maxWidth;
        }

        if (window.MinHeight > maxHeight)
        {
            window.MinHeight = maxHeight;
        }

        if (!double.IsNaN(window.Width) && window.Width > maxWidth)
        {
            window.Width = maxWidth;
        }

        if (!double.IsNaN(window.Height) && window.Height > maxHeight)
        {
            window.Height = maxHeight;
        }

        if (window.ActualWidth > maxWidth)
        {
            window.Width = maxWidth;
        }

        if (window.ActualHeight > maxHeight)
        {
            window.Height = maxHeight;
        }
    }

    private static double LimitMaximum(double currentMaximum, double screenMaximum)
    {
        return double.IsNaN(currentMaximum) || double.IsInfinity(currentMaximum)
            ? screenMaximum
            : Math.Min(currentMaximum, screenMaximum);
    }

    private static void MoveWorkspaceWindowIntoWorkArea(Window window)
    {
        if (!window.IsVisible || double.IsNaN(window.Left) || double.IsNaN(window.Top))
        {
            return;
        }

        var workArea = SystemParameters.WorkArea;
        var width = window.ActualWidth > 0 ? window.ActualWidth : window.Width;
        var height = window.ActualHeight > 0 ? window.ActualHeight : window.Height;

        if (!double.IsNaN(width) && window.Left + width > workArea.Right)
        {
            window.Left = Math.Max(workArea.Left, workArea.Right - width);
        }

        if (!double.IsNaN(height) && window.Top + height > workArea.Bottom)
        {
            window.Top = Math.Max(workArea.Top, workArea.Bottom - height);
        }

        if (window.Left < workArea.Left)
        {
            window.Left = workArea.Left;
        }

        if (window.Top < workArea.Top)
        {
            window.Top = workArea.Top;
        }
    }

    private static void EnsureWorkspaceWindowContentScrollable(Window window)
    {
        if (window.Content is not UIElement content || content is ScrollViewer)
        {
            return;
        }

        window.Content = null;
        window.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            PanningMode = PanningMode.Both,
            Focusable = false
        };
    }

    private static void HandleWorkspaceWindowStateChanged(object? sender, EventArgs e)
    {
        if (sender is not Window window || !IsWorkspaceChildWindow(window) || window.WindowState != WindowState.Minimized)
        {
            return;
        }

        window.Dispatcher.BeginInvoke(
            () =>
            {
                EnableVisibleApplicationWindows();
                if (Current.MainWindow is Window mainWindow && mainWindow.IsVisible)
                {
                    mainWindow.Activate();
                }
            },
            DispatcherPriority.Send);
    }

    private static void HandleWorkspaceWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Handled || e.Key != Key.Escape || sender is not Window window || !IsWorkspaceChildWindow(window))
        {
            return;
        }

        e.Handled = true;
        try
        {
            window.DialogResult = false;
        }
        catch (InvalidOperationException)
        {
            window.Close();
        }
    }

    private static bool IsWorkspaceChildWindow(Window window)
    {
        return Current.MainWindow is Window mainWindow
               && mainWindow.IsVisible
               && !ReferenceEquals(window, mainWindow)
               && window is not StartupLoadingWindow;
    }

    private static void EnableVisibleApplicationWindows()
    {
        foreach (Window window in Current.Windows)
        {
            if (window.IsVisible && !window.IsEnabled)
            {
                window.IsEnabled = true;
            }
        }
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
