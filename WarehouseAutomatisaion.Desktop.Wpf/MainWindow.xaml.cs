using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarehouseAutomatisaion.Desktop.Data;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class MainWindow : Window
{
    private const double SidebarExpandedWidth = 214d;
    private const double SidebarCollapsedWidth = 74d;
    private const string AdminRoleCode = "admin";
    private const string ManagerRoleCode = "manager";
    private const int SalesWorkspaceAutosaveDelayMilliseconds = 5000;
    private const int MaxOpenSectionTabs = 5;
    private const int MaxOpenDynamicEditorTabs = 6;

    private static readonly WpfBrush ActiveNavBackground = BrushFromHex("#EEF2FF");
    private static readonly WpfBrush ActiveNavBorder = BrushFromHex("#C9D3F7");
    private static readonly WpfBrush ActiveNavForeground = BrushFromHex("#2F45D3");

    private static readonly WpfBrush DefaultNavBackground = WpfBrushes.Transparent;
    private static readonly WpfBrush DefaultNavBorder = BrushFromHex("#E3E8F2");
    private static readonly WpfBrush DefaultNavForeground = BrushFromHex("#1B2740");
    private static readonly HashSet<string> LazyUnloadSectionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "sales",
        "customers",
        "shipments",
        "finance",
        "purchasing",
        "warehouse",
        "stock",
        "cells",
        "catalog",
        "audit"
    };

    private readonly Dictionary<string, SectionDefinition> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabItem> _tabsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WpfButton> _navButtonsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DynamicTabDefinition> _dynamicTabsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _tabAccessOrder = new(StringComparer.OrdinalIgnoreCase);

    private readonly DemoWorkspace _demoWorkspace;
    private readonly DesktopClientStartupResult _startupStatus;
    private readonly SalesWorkspaceStore _salesWorkspaceStore;
    private readonly SalesWorkspace _salesWorkspace;
    private readonly FunctionalCoverageSnapshot _coverage;
    private readonly ApplicationUpdateService _applicationUpdateService;
    private readonly DispatcherTimer _salesWorkspaceAutosaveTimer;
    private readonly DispatcherTimer _remoteSalesRefreshTimer;
    private readonly bool _loadRemoteWorkspaceAfterStartup;

    private ApplicationRelease? _availableRelease;
    private bool _updateOperationInProgress;
    private bool _remoteSalesRefreshInProgress;
    private bool _applyingRemoteSalesRefresh;
    private bool _salesWorkspaceSaveBlockedUntilRemoteLoad;
    private bool _salesWorkspaceSaveInProgress;
    private bool _salesWorkspaceSaveQueued;
    private bool _salesWorkspaceSaveWarningShown;
    private bool _isSidebarCollapsed;
    private long _nextTabAccessStamp;
    private string _currentRoleCode = ManagerRoleCode;

    public MainWindow(DesktopClientStartupResult startupStatus)
        : this(startupStatus, SalesWorkspaceStore.CreateDefault(), null)
    {
    }

    internal MainWindow(DesktopClientStartupResult startupStatus, MainWindowStartupData startupData)
        : this(
            startupStatus,
            startupData.SalesWorkspaceStore,
            startupData.SalesWorkspace,
            startupData.LoadRemoteWorkspaceAfterStartup)
    {
    }

    private MainWindow(
        DesktopClientStartupResult startupStatus,
        SalesWorkspaceStore salesWorkspaceStore,
        SalesWorkspace? salesWorkspace,
        bool loadRemoteWorkspaceAfterStartup = false)
    {
        _startupStatus = startupStatus;
        _loadRemoteWorkspaceAfterStartup = loadRemoteWorkspaceAfterStartup;
        _salesWorkspaceSaveBlockedUntilRemoteLoad = loadRemoteWorkspaceAfterStartup;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        EnsureWindowFitsWorkArea();
        Title = AppBranding.ProductName;
        ApplicationNameText.Text = AppBranding.ProductName;
        InitializeUserProfile();

        _demoWorkspace = DemoWorkspace.Create();
        _salesWorkspaceStore = salesWorkspaceStore;
        _salesWorkspace = salesWorkspace ?? TryLoadSalesWorkspace(_salesWorkspaceStore, _startupStatus.UserName);
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        _coverage = FunctionalCoverageSnapshot.Create();
        _applicationUpdateService = new ApplicationUpdateService();
        _salesWorkspaceAutosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(SalesWorkspaceAutosaveDelayMilliseconds)
        };
        _salesWorkspaceAutosaveTimer.Tick += HandleSalesWorkspaceAutosaveTimerTick;
        _remoteSalesRefreshTimer = new DispatcherTimer
        {
            // Release 1.0.134: 15s → 60s. Раньше каждые 15 сек тянулись 2000 заказов
            // (после 1.0.132) — ~700 ms сети. За час: 240 × 700ms = 168 секунд занятого сокета.
            // Реальные правки контрагентами происходят редко, 1 минута между refresh — норма.
            Interval = TimeSpan.FromSeconds(60)
        };
        _remoteSalesRefreshTimer.Tick += HandleRemoteSalesRefreshTimerTick;

        RegisterSidebarButtons();
        RegisterSections();
        ApplyAuthorization();
        InitializeDatabaseStatus();
        InitializeUpdatePanel();
        // WMS pivot: первая страница — Остатки (главная для оператора склада).
        // Раньше был dashboard с накопителями legacy Закупки/Продажи.
        OpenSection("stock");

        InitializeTrayIcon();

        Loaded += HandleWindowLoaded;
    }

    // Release 1.0.128: системный трей. При закрытии окна сворачиваемся вместо exit'а,
    // чтобы держать прогретый кэш Backplane и не пересоединяться к серверу заново.
    // Окончательный выход — через меню трея «Выход» или Alt+F4 повторно при свёрнутом
    // (флаг _shutdownConfirmed).
    private System.Windows.Forms.NotifyIcon? _trayIcon;
    private bool _shutdownConfirmed;

    private void InitializeTrayIcon()
    {
        try
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = AppBranding.ProductName,
                Visible = true,
                Icon = LoadTrayIcon()
            };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Открыть Major");
            openItem.Click += (_, _) => RestoreFromTray();
            menu.Items.Add(openItem);
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Выход");
            exitItem.Click += (_, _) =>
            {
                _shutdownConfirmed = true;
                Close();
            };
            menu.Items.Add(exitItem);
            _trayIcon.ContextMenuStrip = menu;
            _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        }
        catch (Exception ex)
        {
            App.WriteClientErrorLog(ex, "InitializeTrayIcon");
            _trayIcon = null;
        }
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        // Используем тот же major.ico (включён в Content + CopyToOutputDirectory).
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "major.ico");
        return System.IO.File.Exists(iconPath)
            ? new System.Drawing.Icon(iconPath)
            : System.Drawing.SystemIcons.Application;
    }

    private void RestoreFromTray()
    {
        try
        {
            Show();
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            Activate();
            Topmost = true;
            Topmost = false;
            Focus();
        }
        catch (Exception ex)
        {
            App.WriteClientErrorLog(ex, "RestoreFromTray");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Release 1.0.128: клик «×» сворачивает в трей и сохраняет session/workspace.
        // Реальный выход — через меню трея «Выход» (_shutdownConfirmed=true) или
        // программный Application.Shutdown.
        if (!_shutdownConfirmed && _trayIcon is not null)
        {
            e.Cancel = true;
            try
            {
                Hide();
                TrySaveSalesWorkspace(showWarning: false);
                _trayIcon.BalloonTipTitle = AppBranding.ProductName;
                _trayIcon.BalloonTipText = "Major работает в фоне. Двойной клик — открыть.";
                _trayIcon.ShowBalloonTip(1500);
            }
            catch (Exception ex)
            {
                App.WriteClientErrorLog(ex, "MainWindow.OnClosing(minimize)");
            }
            return;
        }

        base.OnClosing(e);
        Loaded -= HandleWindowLoaded;
        if (_salesWorkspace is not null)
        {
            _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
        }

        if (_salesWorkspaceAutosaveTimer is not null)
        {
            _salesWorkspaceAutosaveTimer.Stop();
            _salesWorkspaceAutosaveTimer.Tick -= HandleSalesWorkspaceAutosaveTimerTick;
        }

        if (_remoteSalesRefreshTimer is not null)
        {
            _remoteSalesRefreshTimer.Stop();
            _remoteSalesRefreshTimer.Tick -= HandleRemoteSalesRefreshTimerTick;
        }

        foreach (var tab in _tabsByKey.Values)
        {
            if (tab.Content is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        try
        {
            TrySaveSalesWorkspace(showWarning: true);
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "MainWindow.OnClosing");
            MessageBox.Show(
                this,
                $"Не удалось сохранить изменения при закрытии приложения.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _applicationUpdateService?.Dispose();
            try
            {
                if (_trayIcon is not null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                    _trayIcon = null;
                }
            }
            catch { }
        }
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_applyingRemoteSalesRefresh || _salesWorkspaceSaveBlockedUntilRemoteLoad)
        {
            return;
        }

        _salesWorkspaceAutosaveTimer.Stop();
        _salesWorkspaceAutosaveTimer.Start();
    }

    private async void HandleSalesWorkspaceAutosaveTimerTick(object? sender, EventArgs e)
    {
        _salesWorkspaceAutosaveTimer.Stop();
        await TrySaveSalesWorkspaceAsync(showWarning: false);
    }

    private async Task<bool> TrySaveSalesWorkspaceAsync(bool showWarning)
    {
        if (_salesWorkspaceSaveBlockedUntilRemoteLoad)
        {
            return false;
        }

        if (_salesWorkspaceSaveInProgress)
        {
            _salesWorkspaceSaveQueued = true;
            return false;
        }

        _salesWorkspaceSaveInProgress = true;
        try
        {
            var snapshot = SalesWorkspaceSnapshot.FromWorkspace(_salesWorkspace);
            var currentOperator = _salesWorkspace.CurrentOperator;
            await Task.Run(() => _salesWorkspaceStore.SaveSnapshot(snapshot, currentOperator));
            _salesWorkspaceSaveWarningShown = false;
            return true;
        }
        catch (Exception exception)
        {
            if (showWarning || !_salesWorkspaceSaveWarningShown)
            {
                _salesWorkspaceSaveWarningShown = true;
                MessageBox.Show(
                    this,
                    $"Не удалось сохранить изменения заказов. Проверьте доступ к базе данных и права на запись.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
        finally
        {
            _salesWorkspaceSaveInProgress = false;
            if (_salesWorkspaceSaveQueued)
            {
                _salesWorkspaceSaveQueued = false;
                _salesWorkspaceAutosaveTimer.Stop();
                _salesWorkspaceAutosaveTimer.Start();
            }
        }
    }

    private bool TrySaveSalesWorkspace(bool showWarning)
    {
        if (_salesWorkspaceSaveBlockedUntilRemoteLoad)
        {
            return false;
        }

        try
        {
            _salesWorkspaceStore.Save(_salesWorkspace);
            _salesWorkspaceSaveWarningShown = false;
            return true;
        }
        catch (Exception exception)
        {
            if (showWarning || !_salesWorkspaceSaveWarningShown)
            {
                _salesWorkspaceSaveWarningShown = true;
                MessageBox.Show(
                    this,
                    $"Не удалось сохранить изменения заказов. Проверьте доступ к базе данных и права на запись.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            return false;
        }
    }

    private async void HandleRemoteSalesRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!_salesWorkspaceStore.IsServerModeEnabled
            || _remoteSalesRefreshInProgress
            || _salesWorkspaceSaveInProgress
            || _salesWorkspaceAutosaveTimer.IsEnabled)
        {
            return;
        }

        _remoteSalesRefreshInProgress = true;
        try
        {
            var hasRemoteChanges = await Task.Run(_salesWorkspaceStore.HasRemoteChanges);
            if (!hasRemoteChanges)
            {
                return;
            }

            await RefreshSalesWorkspaceFromServerCoreAsync(showStatus: true);
        }
        catch
        {
        }
        finally
        {
            _remoteSalesRefreshInProgress = false;
        }
    }

    private async Task RefreshSalesWorkspaceFromServerAsync(bool showStatus)
    {
        if (_remoteSalesRefreshInProgress)
        {
            return;
        }

        _remoteSalesRefreshInProgress = true;
        try
        {
            await RefreshSalesWorkspaceFromServerCoreAsync(showStatus);
        }
        finally
        {
            _remoteSalesRefreshInProgress = false;
        }
    }

    private async Task RefreshSalesWorkspaceFromServerCoreAsync(bool showStatus)
    {
        _applyingRemoteSalesRefresh = true;
        try
        {
            if (showStatus && _salesWorkspaceSaveBlockedUntilRemoteLoad)
            {
                ApplicationUpdateStatusText.Text = "Данные заказов загружаются из общей базы в фоне...";
            }

            if (_salesWorkspaceStore.HasPendingLocalSync)
            {
                if (showStatus)
                {
                    ApplicationUpdateStatusText.Text = "Локальные изменения заказов отправляются в общую БД...";
                }

                var currentOperator = string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator)
                    ? Environment.UserName
                    : _salesWorkspace.CurrentOperator;
                _ = await Task.Run(() => _salesWorkspaceStore.TrySyncPendingLocalSnapshot(currentOperator));
            }

            var refreshed = await Task.Run(() => _salesWorkspaceStore.TryRefreshFromBackplane(_salesWorkspace));
            if (!refreshed)
            {
                return;
            }

            _salesWorkspaceSaveBlockedUntilRemoteLoad = false;
            if (showStatus)
            {
                ApplicationUpdateStatusText.Text = "Данные заказов обновлены из общей базы.";
            }
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, "MainWindow.RefreshSalesWorkspaceFromServerAsync");
            if (showStatus && _salesWorkspaceSaveBlockedUntilRemoteLoad)
            {
                ApplicationUpdateStatusText.Text = "Не удалось быстро загрузить данные заказов из общей базы.";
            }
        }
        finally
        {
            _applyingRemoteSalesRefresh = false;
        }
    }

    private static WpfSolidColorBrush BrushFromHex(string hex)
    {
        return (WpfSolidColorBrush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
    }

    private void EnsureWindowFitsWorkArea()
    {
        var area = SystemParameters.WorkArea;
        var targetWidth = Math.Min(Width, Math.Max(1100d, area.Width - 24d));
        var targetHeight = Math.Min(Height, Math.Max(700d, area.Height - 24d));

        Width = targetWidth;
        Height = targetHeight;
        Left = area.Left + (area.Width - targetWidth) / 2d;
        Top = area.Top + (area.Height - targetHeight) / 2d;
    }

    private void InitializeUserProfile()
    {
        _currentRoleCode = NormalizeRoleCode(_startupStatus.PrimaryRoleCode);
        var displayName = string.IsNullOrWhiteSpace(_startupStatus.DisplayName)
            ? Environment.UserName
            : _startupStatus.DisplayName.Trim();
        var roleName = GetRoleDisplayName(_currentRoleCode);

        UserDisplayNameText.Text = displayName;
        UserRoleText.Text = roleName;
        UserAvatarText.Text = BuildInitials(displayName);
        UserProfileCard.ToolTip = $"{displayName}\n{roleName}";
        UserProfileCard.ContextMenu = BuildUserProfileMenu();
    }

    private ContextMenu BuildUserProfileMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem
        {
            Header = UserDisplayNameText.Text,
            IsEnabled = false
        });
        menu.Items.Add(new Separator());

        menu.Items.Add(CreateRoleMenuItem(AdminRoleCode));
        menu.Items.Add(CreateRoleMenuItem(ManagerRoleCode));
        return menu;
    }

    private MenuItem CreateRoleMenuItem(string roleCode)
    {
        var normalizedRoleCode = NormalizeRoleCode(roleCode);
        var item = new MenuItem
        {
            Header = GetRoleDisplayName(normalizedRoleCode),
            Tag = normalizedRoleCode,
            IsCheckable = true,
            IsChecked = string.Equals(_currentRoleCode, normalizedRoleCode, StringComparison.OrdinalIgnoreCase),
            IsEnabled = CanUseRole(normalizedRoleCode)
        };
        item.Click += HandleRoleMenuItemClick;
        return item;
    }

    private void HandleRoleMenuItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string roleCode } || !CanUseRole(roleCode))
        {
            return;
        }

        _currentRoleCode = NormalizeRoleCode(roleCode);
        UserRoleText.Text = GetRoleDisplayName(_currentRoleCode);
        UserProfileCard.ToolTip = $"{UserDisplayNameText.Text}\n{UserRoleText.Text}";
        UserProfileCard.ContextMenu = BuildUserProfileMenu();
        ApplyAuthorization();
    }

    private void HandleUserProfileCardClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (UserProfileCard.ContextMenu is null)
        {
            UserProfileCard.ContextMenu = BuildUserProfileMenu();
        }

        UserProfileCard.ContextMenu.PlacementTarget = UserProfileCard;
        UserProfileCard.ContextMenu.IsOpen = true;
    }

    private void ApplySidebarState()
    {
        // Сворачивание сайдбара убрано — кнопка заменена на «История посещений».
        _isSidebarCollapsed = false;
        SidebarColumn.Width = new GridLength(SidebarExpandedWidth);
        SidebarRoot.Margin = new Thickness(16, 18, 16, 16);
        ApplicationNameText.Visibility = Visibility.Visible;
        SidebarNavigationTitleText.Visibility = Visibility.Visible;
        UserProfileTextPanel.Visibility = Visibility.Visible;
        UserProfileChevronText.Visibility = Visibility.Visible;
        UpdatePanelCard.Visibility = Visibility.Visible;
        UserProfileCard.Padding = new Thickness(14);
        UserProfileCard.Width = double.NaN;
        UserProfileCard.HorizontalAlignment = HorizontalAlignment.Stretch;

        foreach (var button in _navButtonsByKey.Values)
        {
            ApplyNavButtonSidebarState(button);
        }
    }

    private void ApplyNavButtonSidebarState(WpfButton button)
    {
        button.Padding = _isSidebarCollapsed ? new Thickness(0) : new Thickness(14, 0, 0, 0);
        button.HorizontalContentAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;

        if (button.Content is not StackPanel panel)
        {
            return;
        }

        panel.HorizontalAlignment = _isSidebarCollapsed ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        foreach (var textBlock in panel.Children.OfType<TextBlock>())
        {
            if (IsIconFont(textBlock.FontFamily.Source))
            {
                textBlock.Margin = new Thickness(0);
                continue;
            }

            textBlock.Visibility = _isSidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
            textBlock.Margin = _isSidebarCollapsed ? new Thickness(0) : new Thickness(12, 0, 0, 0);
        }
    }

    private void ApplyAuthorization()
    {
        var isAdmin = string.Equals(_currentRoleCode, AdminRoleCode, StringComparison.OrdinalIgnoreCase);
        NavSettingsButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        if (!isAdmin
            && WorkspaceTabs.SelectedItem is TabItem { Tag: string selectedKey }
            && string.Equals(selectedKey, "settings", StringComparison.OrdinalIgnoreCase))
        {
            OpenSection("stock");
        }
    }

    private bool CanUseRole(string roleCode)
    {
        var normalizedRoleCode = NormalizeRoleCode(roleCode);
        var assignedRoles = _startupStatus.RoleCodes ?? Array.Empty<string>();
        var hasRole = assignedRoles.Any(item => string.Equals(NormalizeRoleCode(item), normalizedRoleCode, StringComparison.OrdinalIgnoreCase));
        var isAssignedAdmin = assignedRoles.Any(item => string.Equals(NormalizeRoleCode(item), AdminRoleCode, StringComparison.OrdinalIgnoreCase));
        return hasRole || isAssignedAdmin && string.Equals(normalizedRoleCode, ManagerRoleCode, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeRoleCode(string? roleCode)
    {
        return string.Equals(roleCode, AdminRoleCode, StringComparison.OrdinalIgnoreCase)
            ? AdminRoleCode
            : ManagerRoleCode;
    }

    private static string GetRoleDisplayName(string roleCode)
    {
        return string.Equals(roleCode, AdminRoleCode, StringComparison.OrdinalIgnoreCase)
            ? "Администратор"
            : "Менеджер";
    }

    private static string BuildInitials(string value)
    {
        var words = (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (words.Length == 0)
        {
            return "MW";
        }

        var initials = string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
        return initials.Length > 0 ? initials : "MW";
    }

    private static bool IsIconFont(string fontFamily)
    {
        return fontFamily.Contains("Segoe Fluent Icons", StringComparison.OrdinalIgnoreCase)
               || fontFamily.Contains("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase);
    }

    private static SalesWorkspace TryLoadSalesWorkspace(SalesWorkspaceStore store, string currentOperator)
    {
        try
        {
            return store.LoadOrCreate(string.IsNullOrWhiteSpace(currentOperator) ? Environment.UserName : currentOperator);
        }
        catch when (!store.IsRemoteDatabaseRequired)
        {
            return SalesWorkspace.Create(string.IsNullOrWhiteSpace(currentOperator) ? Environment.UserName : currentOperator);
        }
    }

    internal static Task<MainWindowStartupData> LoadStartupDataAsync(DesktopClientStartupResult startupStatus)
    {
        return Task.Run(() =>
        {
            var store = SalesWorkspaceStore.CreateDefault();
            var workspace = TryLoadSalesWorkspace(store, startupStatus.UserName);
            return new MainWindowStartupData(store, workspace, LoadRemoteWorkspaceAfterStartup: false);
        });
    }

    internal static MainWindowStartupData CreateDeferredStartupData(DesktopClientStartupResult startupStatus)
    {
        var store = SalesWorkspaceStore.CreateDefault();
        var operatorName = string.IsNullOrWhiteSpace(startupStatus.UserName)
            ? Environment.UserName
            : startupStatus.UserName;
        var workspace = store.LoadServerCacheOrCreate(operatorName);
        return new MainWindowStartupData(
            store,
            workspace,
            LoadRemoteWorkspaceAfterStartup: store.IsServerModeEnabled);
    }

    private void InitializeUpdatePanel()
    {
        ApplicationVersionText.Text = $"Версия {AppBranding.CurrentVersion}";
        SetUpdatePanelState(
            buttonText: "Проверить обновления",
            statusText: _applicationUpdateService.IsEnabled
                ? "Проверяю канал обновлений..."
                : "Автообновление отключено в конфиге.",
            buttonEnabled: true);
    }

    private void InitializeDatabaseStatus()
    {
        if (_startupStatus.UsesSharedDatabase)
        {
            DatabaseStatusBadge.Background = BrushFromHex("#EAF8F0");
            DatabaseStatusBadge.BorderBrush = BrushFromHex("#BFE8CF");
            DatabaseStatusIconText.Text = "\uE930";
            DatabaseStatusIconText.Foreground = BrushFromHex("#1F8F50");
            DatabaseStatusTitleText.Text = "Общая база";
            DatabaseStatusText.Text = $"{_startupStatus.Host}:{_startupStatus.Port} / {_startupStatus.Database}";
            return;
        }

        DatabaseStatusBadge.Background = BrushFromHex("#FFF4E3");
        DatabaseStatusBadge.BorderBrush = BrushFromHex("#FFD9A3");
        DatabaseStatusIconText.Text = "\uE783";
        DatabaseStatusIconText.Foreground = BrushFromHex("#B76600");
        DatabaseStatusTitleText.Text = "Локальные данные";
        DatabaseStatusText.Text = "Изменения видны только на этом рабочем месте.";
    }

    private async void HandleWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_salesWorkspaceStore.IsServerModeEnabled)
        {
            _remoteSalesRefreshTimer.Start();
            if (_loadRemoteWorkspaceAfterStartup)
            {
                _ = RefreshSalesWorkspaceFromServerAsync(showStatus: true);
            }
        }

        // Release 1.0.128: после старта прогреваем кэш Purchasing/Warehouse в фоне,
        // чтобы при первом клике по «Закупки → Заказы поставщикам» / «Склад → …»
        // не было 5-15 сек спиннера. Загрузка идёт через те же CreateDefault().LoadOrCreate,
        // которые после 1.0.127 кладут результат в static-кэш. Ошибки не блокируют
        // UI — пользователь просто увидит спиннер при первом клике, как до фикса.
        // Release 1.0.129: добавлен Catalog (process cache + lazy item_prices).
        _ = PrewarmOperationalCachesAsync();

        await RefreshUpdateStateAsync(showDialogOnNonUpdateResult: false);
    }

    private async Task PrewarmOperationalCachesAsync()
    {
        if (_salesWorkspace is null)
        {
            return;
        }

        var op = _salesWorkspace.CurrentOperator ?? string.Empty;
        var sales = _salesWorkspace;
        try
        {
            await Task.Run(() =>
            {
                // Release 1.0.129: pool warmup — открываем 3 connection'а
                // параллельно, чтобы MySqlConnector pool накопил прогретые
                // TLS-сессии. Первый клик пользователя через ~0.3 сек получит
                // уже готовое соединение вместо 200 ms TLS-handshake.
                Parallel.For(0, 3, _ =>
                {
                    try
                    {
                        using var c = new MySqlConnector.MySqlConnection(
                            "Server=147.45.108.97;Port=3306;Database=warehouse_automation;User Id=majorwarehause_app;Password=xfn0ZqwGkTzNaX_lqPBjMe178EqxY8G9LcZsE6IWa6Y;ConnectionTimeout=10;Pooling=true;MinimumPoolSize=3;");
                        c.Open();
                        using var ping = c.CreateCommand();
                        ping.CommandText = "SELECT 1";
                        ping.ExecuteScalar();
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Prewarm] pool: {ex.Message}"); }
                });

                try
                {
                    Data.PurchasingOperationalWorkspaceStore.CreateDefault().LoadOrCreate(op, sales);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Prewarm] purchasing: {ex.Message}");
                }
                try
                {
                    Data.WarehouseOperationalWorkspaceStore.CreateDefault().LoadOrCreate(op, sales);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Prewarm] warehouse: {ex.Message}");
                }
                try
                {
                    Data.CatalogWorkspaceStore.CreateDefault().LoadOrCreate(op, sales);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Prewarm] catalog: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Prewarm] failed: {ex}");
        }
    }

    private async Task RefreshUpdateStateAsync(bool showDialogOnNonUpdateResult)
    {
        if (_updateOperationInProgress)
        {
            return;
        }

        _updateOperationInProgress = true;
        try
        {
            SetUpdatePanelState("Проверяю...", "Проверяю наличие новой версии...", false);
            var result = await _applicationUpdateService.CheckForUpdatesAsync();
            ApplyUpdateCheckResult(result, showDialogOnNonUpdateResult);
        }
        catch (Exception exception)
        {
            _availableRelease = null;
            SetUpdatePanelState("Проверить обновления", exception.Message, true);

            if (showDialogOnNonUpdateResult)
            {
                MessageBox.Show(
                    this,
                    exception.Message,
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _updateOperationInProgress = false;
        }
    }

    private void ApplyUpdateCheckResult(ApplicationUpdateCheckResult result, bool showDialogOnNonUpdateResult)
    {
        _availableRelease = result.State == ApplicationUpdateCheckState.UpdateAvailable
            ? result.Release
            : null;

        switch (result.State)
        {
            case ApplicationUpdateCheckState.UpdateAvailable:
                SetUpdatePanelState(
                    $"Установить {result.Release?.Version ?? "обновление"}",
                    result.Message,
                    true);
                break;
            case ApplicationUpdateCheckState.UpToDate:
                SetUpdatePanelState("Проверить обновления", result.Message, true);
                if (showDialogOnNonUpdateResult)
                {
                    MessageBox.Show(
                        this,
                        result.Message,
                        AppBranding.MessageBoxTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                break;
            case ApplicationUpdateCheckState.Disabled:
                _availableRelease = null;
                SetUpdatePanelState("Проверить обновления", result.Message, true);
                if (showDialogOnNonUpdateResult)
                {
                    MessageBox.Show(
                        this,
                        result.Message,
                        AppBranding.MessageBoxTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                break;
            default:
                _availableRelease = null;
                SetUpdatePanelState("Проверить обновления", result.Message, true);
                if (showDialogOnNonUpdateResult)
                {
                    MessageBox.Show(
                        this,
                        result.Message,
                        AppBranding.MessageBoxTitle,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                break;
        }
    }

    private void SetUpdatePanelState(string buttonText, string statusText, bool buttonEnabled)
    {
        UpdateApplicationButton.Content = buttonText;
        UpdateApplicationButton.IsEnabled = buttonEnabled;
        ApplicationUpdateStatusText.Text = statusText;
    }

    private void RegisterSidebarButtons()
    {
        // Маппинг section-key → sidebar button. Подразделы (sales/customers/shipments/...)
        // подсвечивают родительский крупный блок (section-sales/section-purchasing/section-warehouse).
        _navButtonsByKey["dashboard"] = NavDashboardButton;
        _navButtonsByKey["audit"] = NavAuditButton;
        _navButtonsByKey["settings"] = NavSettingsButton;

        _navButtonsByKey[NavigationCommandCatalog.SalesSectionKey] = NavSalesButton;
        _navButtonsByKey["sales"] = NavSalesButton;
        _navButtonsByKey["customers"] = NavSalesButton;
        _navButtonsByKey["shipments"] = NavSalesButton;
        _navButtonsByKey["finance"] = NavSalesButton;

        _navButtonsByKey[NavigationCommandCatalog.PurchasingSectionKey] = NavPurchasingButton;
        _navButtonsByKey["purchasing"] = NavPurchasingButton;

        _navButtonsByKey[NavigationCommandCatalog.WarehouseSectionKey] = NavWarehouseButton;
        _navButtonsByKey["warehouse"] = NavWarehouseButton;

        // Sprint 1 (WMS остатки): отдельная кнопка для живых остатков (app_warehouse_stock_balances).
        _navButtonsByKey["stock"] = NavStockBalancesButton;

        // Sprint 3 (WMS ячейки): кнопка для master-data ячеек склада.
        _navButtonsByKey["cells"] = NavStorageCellsButton;

        // catalog (Товары) — общий пункт для всех 3 разделов, без выделения в сайдбаре.
    }

    private void RegisterSections()
    {
        _sections["dashboard"] = new SectionDefinition(
            Key: "dashboard",
            Caption: "Главная",
            Subtitle: "Рабочая панель с быстрыми переходами, задачами и аналитикой.",
            Closable: false,
            Factory: CreateDashboardView);

        _sections["sales"] = new SectionDefinition(
            Key: "sales",
            Caption: "Заказы покупателей",
            Subtitle: "Список заказов в стиле 1С УНФ — даты, состояние, отгрузка, оплата.",
            Closable: true,
            Factory: () => new SalesOrdersWorkspaceView(_salesWorkspace));

        _sections["customers"] = new SectionDefinition(
            Key: "customers",
            Caption: "Контрагенты",
            Subtitle: "База покупателей, поставщиков и прочих контрагентов.",
            Closable: true,
            Factory: () => new ContractorsWorkspaceView(_salesWorkspace));

        _sections["invoices"] = new SectionDefinition(
            Key: "invoices",
            Caption: "Счета на оплату",
            Subtitle: "Список счетов покупателей в стиле 1С УНФ — даты, оплата, ЭДО.",
            Closable: true,
            Factory: () => new SalesInvoicesWorkspaceView(_salesWorkspace));

        _sections["shipments"] = new SectionDefinition(
            Key: "shipments",
            Caption: "Расходные накладные",
            Subtitle: "Накладные + заказы покупателей к отгрузке.",
            Closable: true,
            Factory: () => new SalesShipmentsWorkspaceView(_salesWorkspace));

        _sections["finance"] = new SectionDefinition(
            Key: "finance",
            Caption: "Возвраты и оплаты",
            Subtitle: "Возвраты покупателей, кассовые поступления и связи с заказами.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateReturnsAndPayments(_salesWorkspace)));

        _sections["returns"] = new SectionDefinition(
            Key: "returns",
            Caption: "Приходные накладные: возвраты",
            Subtitle: "Возвраты покупателей в стиле 1С УНФ.",
            Closable: true,
            Factory: () => new SalesReturnsWorkspaceView(_salesWorkspace));

        _sections["reconciliations"] = new SectionDefinition(
            Key: "reconciliations",
            Caption: "Сверки взаиморасчетов",
            Subtitle: "Журнал актов сверки с контрагентами.",
            Closable: true,
            Factory: () => new SalesReconciliationsWorkspaceView(_salesWorkspace));

        _sections["priceRegistrations"] = new SectionDefinition(
            Key: "priceRegistrations",
            Caption: "Установка цен",
            Subtitle: "Журнал документов установки цен номенклатуры.",
            Closable: true,
            Factory: () => new PriceRegistrationsWorkspaceView(_salesWorkspace));

        _sections["purchasing"] = new SectionDefinition(
            Key: "purchasing",
            Caption: "Закупки",
            Subtitle: "Поставщики, закупочные заказы и приемка.",
            Closable: true,
            Factory: () => new PurchasingWorkspaceView(_salesWorkspace));

        _sections["warehouse"] = new SectionDefinition(
            Key: "warehouse",
            Caption: "Склад",
            Subtitle: "Остатки, перемещения, резервы и инвентаризация.",
            Closable: true,
            Factory: () => new WarehouseWorkspaceView(_salesWorkspace));

        // Sprint 1 (WMS остатки): новая витрина живых остатков из app_warehouse_stock_balances.
        // В Sprint 2 источник заменится на pull через OData из 1С, UI остаётся без изменений.
        _sections["stock"] = new SectionDefinition(
            Key: "stock",
            Caption: "Остатки",
            Subtitle: "Живые остатки по складам — проекция из stock_balances в app_warehouse_stock_balances.",
            Closable: true,
            Factory: () => new StockBalancesWorkspaceView());

        // Sprint 3 (WMS ячейки): master-data ячеек склада в app_warehouse_storage_cells.
        // CRUD + (Task 21) импорт CSV + (Task 22) печать QR-этикеток.
        _sections["cells"] = new SectionDefinition(
            Key: "cells",
            Caption: "Ячейки",
            Subtitle: "Адресное хранение: зоны, ряды, стеллажи, полки, ячейки. QR-этикетки и импорт.",
            Closable: true,
            Factory: () => new StorageCellsWorkspaceView());

        // Sprint 8 (AI loop closure): черновики AI-распознанных накладных,
        // ожидающие разноски в ячейки склада.
        _sections["receipt-drafts"] = new SectionDefinition(
            Key: "receipt-drafts",
            Caption: "Черновики AI",
            Subtitle: "Распознанные накладные ждут приёмки. Двойной клик — открыть и принять в ячейку.",
            Closable: true,
            Factory: () => new ReceiptDraftsWorkspaceView());

        // Sprint 12 (AI Chat assistant): «Спроси склад» — chat с Claude tool use.
        _sections["assistant"] = new SectionDefinition(
            Key: "assistant",
            Caption: "Спроси склад",
            Subtitle: "AI помощник с доступом к остаткам, ячейкам и рекомендациям через Claude tool use.",
            Closable: true,
            Factory: () => new WarehouseAssistantView());

        _sections["catalog"] = new SectionDefinition(
            Key: "catalog",
            Caption: "Товары",
            Subtitle: "Каталог товаров, категории и состояние наличия.",
            Closable: true,
            Factory: () => new ProductsWorkspaceView(_salesWorkspace));

        _sections["audit"] = new SectionDefinition(
            Key: "audit",
            Caption: "Отчеты",
            Subtitle: "Продажи, статусы и ключевая аналитика по модулям.",
            Closable: true,
            Factory: () => new ReportsWorkspaceView(_salesWorkspace));

        _sections["settings"] = new SectionDefinition(
            Key: "settings",
            Caption: "Настройки",
            Subtitle: "Системные параметры, подключение, обновления и связи данных.",
            Closable: true,
            Factory: () => new SystemSettingsWorkspaceView(
                _startupStatus,
                RecordsWorkspaceCatalog.CreateModel(_coverage, _salesWorkspace),
                _salesWorkspace.Warehouses));

        // 1С УНФ-стиль «Панель разделов»: 3 крупные витрины (Продажи / Закупки / Склад).
        _sections[NavigationCommandCatalog.SalesSectionKey] = new SectionDefinition(
            Key: NavigationCommandCatalog.SalesSectionKey,
            Caption: "Продажи",
            Subtitle: "Витрина раздела «Продажи».",
            Closable: true,
            Factory: () => new SectionOverviewView(NavigationCommandCatalog.Sales));

        _sections[NavigationCommandCatalog.PurchasingSectionKey] = new SectionDefinition(
            Key: NavigationCommandCatalog.PurchasingSectionKey,
            Caption: "Закупки",
            Subtitle: "Витрина раздела «Закупки».",
            Closable: true,
            Factory: () => new SectionOverviewView(NavigationCommandCatalog.Purchasing));

        _sections[NavigationCommandCatalog.WarehouseSectionKey] = new SectionDefinition(
            Key: NavigationCommandCatalog.WarehouseSectionKey,
            Caption: "Склад",
            Subtitle: "Витрина раздела «Склад».",
            Closable: true,
            Factory: () => new SectionOverviewView(NavigationCommandCatalog.Warehouse));
    }

    /// <summary>
    /// Активирует внутреннюю подсекцию workspace-view (Purchasing/Warehouse/Products),
    /// если он уже открыт. Вызывается из <see cref="SectionOverviewView"/> сразу после
    /// <see cref="OpenSection"/>, чтобы провалиться в нужный подраздел.
    /// </summary>
    internal void ActivateSubSection(string sectionKey, string subSectionKey)
    {
        if (string.IsNullOrWhiteSpace(sectionKey) || string.IsNullOrWhiteSpace(subSectionKey))
        {
            return;
        }

        if (!_tabsByKey.TryGetValue(sectionKey, out var tab))
        {
            return;
        }

        var content = tab.Content;
        if (content is ScrollViewer scrollViewer)
        {
            content = scrollViewer.Content;
        }

        switch (content)
        {
            case PurchasingWorkspaceView purchasing:
                purchasing.ActivateSubSection(subSectionKey);
                break;
            case WarehouseWorkspaceView warehouse:
                warehouse.ActivateSubSection(subSectionKey);
                break;
            case ProductsWorkspaceView products:
                products.ActivateSubSection(subSectionKey);
                break;
            case ContractorsWorkspaceView contractors:
                contractors.ActivateSubSection(subSectionKey);
                break;
        }
    }

    /// <summary>
    /// Release 1.0.125: вместо переключения внутри одного umbrella-view (старый
    /// flow OpenSection+ActivateSubSection) открываем подраздел как СВОЁ окно-вкладку.
    /// Это разгружает «жирные» umbrella-views (PurchasingWorkspaceView с 7 секциями
    /// и т.п.): каждая вкладка содержит свой инстанс, фильтры/сорт/пагинация
    /// независимы, утечки между секциями невозможны.
    /// </summary>
    internal void OpenSubSectionTab(string sectionKey, string subSectionKey, string caption)
    {
        if (string.IsNullOrWhiteSpace(sectionKey) || string.IsNullOrWhiteSpace(subSectionKey))
        {
            return;
        }

        var tabKey = $"{sectionKey}:{subSectionKey}";
        OpenWorkspaceEditorTab(
            tabKey,
            string.IsNullOrWhiteSpace(caption) ? subSectionKey : caption,
            string.Empty,
            () =>
            {
                FrameworkElement view = sectionKey.ToLowerInvariant() switch
                {
                    "purchasing" => new PurchasingWorkspaceView(_salesWorkspace),
                    "warehouse" => new WarehouseWorkspaceView(_salesWorkspace),
                    "products" or "catalog" => new ProductsWorkspaceView(_salesWorkspace),
                    "customers" => new ContractorsWorkspaceView(_salesWorkspace),
                    _ => new ContractorsWorkspaceView(_salesWorkspace)
                };

                switch (view)
                {
                    case PurchasingWorkspaceView purchasing:
                        purchasing.ActivateSubSection(subSectionKey);
                        break;
                    case WarehouseWorkspaceView warehouse:
                        warehouse.ActivateSubSection(subSectionKey);
                        break;
                    case ProductsWorkspaceView products:
                        products.ActivateSubSection(subSectionKey);
                        break;
                    case ContractorsWorkspaceView contractors:
                        contractors.ActivateSubSection(subSectionKey);
                        break;
                }

                return view;
            });
    }

    private DashboardWorkspaceView CreateDashboardView()
    {
        var dashboard = new DashboardWorkspaceView(_salesWorkspace, _demoWorkspace);
        dashboard.NavigationRequested += (_, targetKey) =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (!string.IsNullOrWhiteSpace(targetKey))
                {
                    OpenSection(targetKey);
                }
            });
        };

        return dashboard;
    }

    internal void OpenSection(string sectionKey)
    {
        if (!CanOpenSection(sectionKey))
        {
            return;
        }

        if (!_sections.TryGetValue(sectionKey, out var section))
        {
            return;
        }

        if (!_tabsByKey.TryGetValue(sectionKey, out var tab))
        {
            tab = CreateSectionTab(section);
            _tabsByKey[sectionKey] = tab;
            WorkspaceTabs.Items.Add(tab);
        }
        else
        {
            EnsureSectionContentLoaded(sectionKey, tab);
        }

        WorkspaceTabs.SelectedItem = tab;
        MarkTabAccess(sectionKey);
        ApplySelection(sectionKey);
        ReleaseInactiveSectionContent(sectionKey);
        PruneInactiveSectionTabs(sectionKey);
    }

    private bool CanOpenSection(string sectionKey)
    {
        if (!string.Equals(sectionKey, "settings", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(_currentRoleCode, AdminRoleCode, StringComparison.OrdinalIgnoreCase);
    }

    private TabItem CreateSectionTab(SectionDefinition section)
    {
        var tab = new TabItem
        {
            Tag = section.Key,
            Header = CreateTabHeader(section.Key, section.Caption, section.Closable),
            Content = CreateSectionContent(section)
        };
        System.Windows.Automation.AutomationProperties.SetName(tab, section.Caption);
        return tab;
    }

    internal bool OpenWorkspaceEditorTab(
        string key,
        string caption,
        string subtitle,
        Func<FrameworkElement> contentFactory)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (_tabsByKey.TryGetValue(key, out var existingTab))
        {
            WorkspaceTabs.SelectedItem = existingTab;
            MarkTabAccess(key);
            ApplySelection(key);
            ReleaseInactiveSectionContent(key);
            PruneInactiveSectionTabs(key);
            return true;
        }

        if (!CanOpenDynamicEditorTab(key))
        {
            return false;
        }

        var content = contentFactory();
        var tabContent = CreateScrollableEditorTabContent(content);
        WpfTextNormalizer.NormalizeTree(content);
        content.Loaded += (_, _) => WpfTextNormalizer.NormalizeTree(content);

        var tab = new TabItem
        {
            Tag = key,
            Header = CreateTabHeader(key, caption, closable: true),
            Content = tabContent
        };
        System.Windows.Automation.AutomationProperties.SetName(tab, caption);

        _dynamicTabsByKey[key] = new DynamicTabDefinition(caption, subtitle);
        _tabsByKey[key] = tab;
        WorkspaceTabs.Items.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        MarkTabAccess(key);
        ApplySelection(key);
        ReleaseInactiveSectionContent(key);
        PruneInactiveSectionTabs(key);
        return true;
    }

    private static FrameworkElement CreateScrollableEditorTabContent(FrameworkElement content)
    {
        if (content is ScrollViewer)
        {
            return content;
        }

        return new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            CanContentScroll = false,
            PanningMode = PanningMode.VerticalFirst,
            Focusable = false
        };
    }

    internal void CloseWorkspaceTab(string key)
    {
        CloseSection(key);
    }

    private object CreateSectionContent(SectionDefinition section)
    {
        try
        {
            var content = section.Factory();
            if (content is Control control)
            {
                if (content is ProductsWorkspaceView productsWorkspace)
                {
                    productsWorkspace.NavigationRequested += (_, targetKey) =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!string.IsNullOrWhiteSpace(targetKey))
                            {
                                OpenSection(targetKey);
                            }
                        });
                    };
                }
                else if (content is WarehouseWorkspaceView warehouseWorkspace)
                {
                    warehouseWorkspace.NavigationRequested += (_, targetKey) =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!string.IsNullOrWhiteSpace(targetKey))
                            {
                                OpenSection(targetKey);
                            }
                        });
                    };
                }
                else if (content is ReportsWorkspaceView reportsWorkspace)
                {
                    reportsWorkspace.NavigationRequested += (_, targetKey) =>
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (!string.IsNullOrWhiteSpace(targetKey))
                            {
                                OpenSection(targetKey);
                            }
                        });
                    };
                }

                WpfTextNormalizer.NormalizeTree(control);
                control.Loaded += (_, _) => WpfTextNormalizer.NormalizeTree(control);
                return control;
            }

            throw new InvalidOperationException($"Unsupported section content type for '{section.Key}'.");
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, $"CreateSectionContent:{section.Key}");
            return CreateSectionErrorContent(section, exception);
        }
    }

    private void EnsureSectionContentLoaded(string sectionKey, TabItem tab)
    {
        if (tab.Content is not null || !_sections.TryGetValue(sectionKey, out var section))
        {
            return;
        }

        tab.Content = CreateSectionContent(section);
    }

    private bool CanOpenDynamicEditorTab(string requestedKey)
    {
        if (_dynamicTabsByKey.Count < MaxOpenDynamicEditorTabs)
        {
            return true;
        }

        var oldestEditorKey = _dynamicTabsByKey.Keys
            .Where(key => !key.Equals(requestedKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetTabAccessStamp)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(oldestEditorKey)
            && _tabsByKey.TryGetValue(oldestEditorKey, out var oldestEditorTab))
        {
            WorkspaceTabs.SelectedItem = oldestEditorTab;
            MarkTabAccess(oldestEditorKey);
            ApplySelection(oldestEditorKey);
        }

        MessageBox.Show(
            this,
            "Открыто слишком много вкладок редактирования. Закройте одну из рабочих вкладок и повторите действие.",
            AppBranding.MessageBoxTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void MarkTabAccess(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        _tabAccessOrder[key] = ++_nextTabAccessStamp;
    }

    private long GetTabAccessStamp(string key)
    {
        return _tabAccessOrder.TryGetValue(key, out var stamp) ? stamp : 0;
    }

    private void PruneInactiveSectionTabs(string selectedKey)
    {
        var openSectionCount = _tabsByKey.Keys.Count(key => _sections.ContainsKey(key));
        if (openSectionCount <= MaxOpenSectionTabs)
        {
            return;
        }

        var removableKeys = _tabsByKey.Keys
            .Where(key => _sections.ContainsKey(key)
                          && !key.Equals("dashboard", StringComparison.OrdinalIgnoreCase)
                          && !key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase))
            .OrderBy(GetTabAccessStamp)
            .ToList();

        foreach (var key in removableKeys)
        {
            if (openSectionCount <= MaxOpenSectionTabs)
            {
                break;
            }

            if (RemoveTab(key))
            {
                openSectionCount--;
            }
        }
    }

    private void ReleaseInactiveSectionContent(string selectedKey)
    {
        foreach (var pair in _tabsByKey.ToArray())
        {
            if (pair.Key.Equals(selectedKey, StringComparison.OrdinalIgnoreCase)
                || !LazyUnloadSectionKeys.Contains(pair.Key)
                || pair.Value.Content is null)
            {
                continue;
            }

            DisposeTabContent(pair.Key, pair.Value);
            pair.Value.Content = null;
        }
    }

    private void DisposeTabContent(string sectionKey, TabItem tab)
    {
        if (tab.Content is not IDisposable disposable)
        {
            return;
        }

        try
        {
            disposable.Dispose();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, $"DisposeTabContent:{sectionKey}");
        }
    }

    private static FrameworkElement CreateSectionErrorContent(SectionDefinition section, Exception exception)
    {
        var panel = new StackPanel
        {
            MaxWidth = 720
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Раздел не загрузился",
            FontSize = 28,
            FontWeight = FontWeights.SemiBold,
            Foreground = BrushFromHex("#17213A")
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"Не удалось открыть раздел «{section.Caption}». Приложение продолжит работу, подробности записаны в журнал ошибок.",
            Margin = new Thickness(0, 10, 0, 0),
            FontSize = 14,
            Foreground = BrushFromHex("#5F6F95"),
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(16),
            Background = BrushFromHex("#FFF4F4"),
            BorderBrush = BrushFromHex("#F4B6B6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new TextBlock
            {
                Text = exception.Message,
                Foreground = BrushFromHex("#8A1F1F"),
                TextWrapping = TextWrapping.Wrap
            }
        });

        return new Border
        {
            Padding = new Thickness(24),
            Background = BrushFromHex("#F6F8FC"),
            Child = panel
        };
    }

    private object CreateTabHeader(string key, string caption, bool closable)
    {
        var panel = new DockPanel
        {
            LastChildFill = false,
            Margin = new Thickness(2, 0, 2, 0)
        };

        var title = new TextBlock
        {
            Text = caption,
            Margin = new Thickness(8, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 14,
            Foreground = BrushFromHex("#1B2740")
        };

        panel.Children.Add(title);

        if (closable)
        {
            var closeButton = new WpfButton
            {
                Tag = key,
                Content = "×",
                Width = 22,
                Height = 22,
                Margin = new Thickness(2, 2, 4, 2),
                Padding = new Thickness(0),
                BorderThickness = new Thickness(0),
                Background = WpfBrushes.Transparent,
                Foreground = BrushFromHex("#5F6F95"),
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Закрыть вкладку"
            };
            System.Windows.Automation.AutomationProperties.SetName(closeButton, "Закрыть вкладку");
            closeButton.Click += HandleCloseTabClick;
            panel.Children.Add(closeButton);
        }

        return panel;
    }

    private void CloseSection(string sectionKey)
    {
        if (sectionKey.Equals("dashboard", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_tabsByKey.ContainsKey(sectionKey))
        {
            return;
        }

        RemoveTab(sectionKey);

        if (WorkspaceTabs.Items.Count == 0)
        {
            OpenSection("stock");
            return;
        }

        if (WorkspaceTabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is string selectedKey)
        {
            EnsureSectionContentLoaded(selectedKey, selectedTab);
            MarkTabAccess(selectedKey);
            ApplySelection(selectedKey);
            ReleaseInactiveSectionContent(selectedKey);
            PruneInactiveSectionTabs(selectedKey);
        }
        else
        {
            OpenSection("stock");
        }
    }

    private bool RemoveTab(string sectionKey)
    {
        if (!_tabsByKey.TryGetValue(sectionKey, out var tab))
        {
            return false;
        }

        DisposeTabContent(sectionKey, tab);

        WorkspaceTabs.Items.Remove(tab);
        _tabsByKey.Remove(sectionKey);
        _dynamicTabsByKey.Remove(sectionKey);
        _tabAccessOrder.Remove(sectionKey);
        return true;
    }

    private void ApplySelection(string sectionKey)
    {
        if (_sections.TryGetValue(sectionKey, out var section))
        {
            CurrentSectionTitleText.Text = section.Caption;
            CurrentSectionSubtitleText.Text = section.Subtitle;
        }
        else if (_dynamicTabsByKey.TryGetValue(sectionKey, out var dynamicTab))
        {
            CurrentSectionTitleText.Text = dynamicTab.Caption;
            CurrentSectionSubtitleText.Text = dynamicTab.Subtitle;
        }

        // Несколько ключей могут указывать на один и тот же блок сайдбара (подразделы
        // → родительская секция). Определяем активный, затем красим уникальные кнопки.
        _navButtonsByKey.TryGetValue(sectionKey, out var activeButton);
        foreach (var button in _navButtonsByKey.Values.Distinct())
        {
            var active = activeButton is not null && button == activeButton;
            button.Background = active ? ActiveNavBackground : DefaultNavBackground;
            button.BorderBrush = active ? ActiveNavBorder : DefaultNavBorder;
            button.Foreground = active ? ActiveNavForeground : DefaultNavForeground;
            button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        }
    }

    private void HandleNavButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.Tag is string sectionKey)
        {
            // Sprint 4 (WMS приёмка): «Приёмка» — это модальный диалог, а не вкладка.
            // Открываем его поверх текущего workspace и не пушим в навигационную историю табов.
            if (string.Equals(sectionKey, "receive", StringComparison.OrdinalIgnoreCase))
            {
                OpenReceiveStockDialog();
                return;
            }

            // Sprint 6 (WMS перемещение): тоже модальный диалог.
            if (string.Equals(sectionKey, "transfer", StringComparison.OrdinalIgnoreCase))
            {
                OpenTransferStockDialog();
                return;
            }

            // Sprint 7 (WMS инвентаризация): модальный диалог.
            if (string.Equals(sectionKey, "stocktake", StringComparison.OrdinalIgnoreCase))
            {
                OpenStockTakeDialog();
                return;
            }

            OpenSection(sectionKey);
        }
    }

    // Sprint 4: модальное окно приёмки. Строим зависимости через DesktopMySqlBackplaneService напрямую,
    // потому что окно живёт коротко — не нужно регистрировать в DI как singleton.
    private void OpenReceiveStockDialog()
    {
        var backplane = WarehouseAutomatisaion.Desktop.Data.DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.",
                "Приёмка товара",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var catalogReader = new WarehouseAutomatisaion.Desktop.Data.MySqlNomenclatureCatalogReader(backplane);
        var cellCatalog = new WarehouseAutomatisaion.Desktop.Data.MySqlStorageCellCatalog(backplane);
        var stockLocations = new WarehouseAutomatisaion.Desktop.Data.MySqlStockLocationRepository(backplane);

        var window = new ReceiveStockWindow(catalogReader, cellCatalog, stockLocations)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    // Sprint 6: модальное окно перемещения между ячейками.
    private void OpenTransferStockDialog()
    {
        var backplane = WarehouseAutomatisaion.Desktop.Data.DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.",
                "Перемещение между ячейками",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var cellCatalog = new WarehouseAutomatisaion.Desktop.Data.MySqlStorageCellCatalog(backplane);
        var stockLocations = new WarehouseAutomatisaion.Desktop.Data.MySqlStockLocationRepository(backplane);

        var window = new TransferStockWindow(cellCatalog, stockLocations)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    // Sprint 7: модальное окно инвентаризации одной ячейки.
    // Sprint 10: + опциональный AI Claude shelf vision для распознавания фото полки.
    private void OpenStockTakeDialog()
    {
        var backplane = WarehouseAutomatisaion.Desktop.Data.DesktopMySqlBackplaneService.TryCreateDefault();
        if (backplane is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.",
                "Инвентаризация",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var cellCatalog = new WarehouseAutomatisaion.Desktop.Data.MySqlStorageCellCatalog(backplane);
        var stockLocations = new WarehouseAutomatisaion.Desktop.Data.MySqlStockLocationRepository(backplane);

        // Sprint 10: Claude shelf vision (optional — null если ключ не настроен).
        var shelfVision = WarehouseAutomatisaion.Desktop.Data.ShelfVisionFactory.TryCreate();

        var window = new StockTakeWindow(cellCatalog, stockLocations, shelfVision)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    private void HandleWorkspaceTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is string sectionKey)
        {
            EnsureSectionContentLoaded(sectionKey, selectedTab);
            MarkTabAccess(sectionKey);
            ApplySelection(sectionKey);
            ReleaseInactiveSectionContent(sectionKey);
            PruneInactiveSectionTabs(sectionKey);
            PushNavHistory(sectionKey);
            UpdateNavToolbar();
        }
    }

    // ===== Навигация в стиле 1С (Назад / Вперёд / Избранное / История) =====

    private readonly List<string> _navHistory = new();
    private int _navHistoryIndex = -1;
    private bool _suppressNavHistoryPush;
    private readonly HashSet<string> _favoriteTabs = new(StringComparer.OrdinalIgnoreCase);

    private void PushNavHistory(string key)
    {
        if (_suppressNavHistoryPush || string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        // Если уже на этой вкладке — не дублируем
        if (_navHistoryIndex >= 0
            && _navHistoryIndex < _navHistory.Count
            && _navHistory[_navHistoryIndex].Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Если перешли вперёд после Назад — обрезаем «forward» хвост
        if (_navHistoryIndex < _navHistory.Count - 1)
        {
            _navHistory.RemoveRange(_navHistoryIndex + 1, _navHistory.Count - _navHistoryIndex - 1);
        }

        _navHistory.Add(key);
        _navHistoryIndex = _navHistory.Count - 1;

        // Ограничение длины
        const int maxHistory = 50;
        if (_navHistory.Count > maxHistory)
        {
            var trim = _navHistory.Count - maxHistory;
            _navHistory.RemoveRange(0, trim);
            _navHistoryIndex -= trim;
        }
    }

    private void UpdateNavToolbar()
    {
        var hasBack = _navHistoryIndex > 0;
        var hasForward = _navHistoryIndex >= 0 && _navHistoryIndex < _navHistory.Count - 1;

        NavBackButton.IsEnabled = hasBack;
        NavBackButton.Opacity = hasBack ? 1.0 : 0.5;
        NavForwardButton.IsEnabled = hasForward;
        NavForwardButton.Opacity = hasForward ? 1.0 : 0.5;
        NavForwardIconText.Foreground = hasForward
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4F, 0x5B, 0xFF))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xAD, 0xD4));

        var currentKey = WorkspaceTabs.SelectedItem is TabItem t && t.Tag is string k ? k : null;
        var isFavorite = currentKey is not null && _favoriteTabs.Contains(currentKey);
        FavoriteIconText.Text = isFavorite ? "" : ""; // E735 = filled star, E734 = outline star
        FavoriteIconText.Foreground = isFavorite
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF5, 0xB0, 0x00))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x9C, 0xAD, 0xD4));
        FavoriteButton.ToolTip = isFavorite ? "Убрать из избранного" : "Добавить в избранное";

        NavCurrentTabText.Text = currentKey is not null && _tabsByKey.TryGetValue(currentKey, out var tab)
            ? ResolveTabCaption(tab)
            : string.Empty;
    }

    private static string ResolveTabCaption(TabItem tab)
    {
        if (tab.Header is DockPanel panel)
        {
            foreach (var child in panel.Children)
            {
                if (child is TextBlock tb && !string.IsNullOrWhiteSpace(tb.Text) && tb.Text != "×")
                {
                    return tb.Text;
                }
            }
        }
        return tab.Header?.ToString() ?? string.Empty;
    }

    private void HandleNavBackClick(object sender, RoutedEventArgs e)
    {
        if (_navHistoryIndex <= 0)
        {
            return;
        }

        _navHistoryIndex--;
        NavigateToHistoryEntry();
    }

    private void HandleNavForwardClick(object sender, RoutedEventArgs e)
    {
        if (_navHistoryIndex >= _navHistory.Count - 1)
        {
            return;
        }

        _navHistoryIndex++;
        NavigateToHistoryEntry();
    }

    private void NavigateToHistoryEntry()
    {
        if (_navHistoryIndex < 0 || _navHistoryIndex >= _navHistory.Count)
        {
            return;
        }

        var key = _navHistory[_navHistoryIndex];
        if (!_tabsByKey.TryGetValue(key, out var tab))
        {
            // Вкладка уже закрыта — открыть заново (только секции)
            if (_sections.ContainsKey(key))
            {
                _suppressNavHistoryPush = true;
                try { OpenSection(key); }
                finally { _suppressNavHistoryPush = false; }
            }
            UpdateNavToolbar();
            return;
        }

        _suppressNavHistoryPush = true;
        try
        {
            WorkspaceTabs.SelectedItem = tab;
        }
        finally
        {
            _suppressNavHistoryPush = false;
        }
        UpdateNavToolbar();
    }

    private void HandleFavoriteClick(object sender, RoutedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is not TabItem tab || tab.Tag is not string key)
        {
            return;
        }

        if (_favoriteTabs.Contains(key))
        {
            _favoriteTabs.Remove(key);
        }
        else
        {
            _favoriteTabs.Add(key);
        }
        UpdateNavToolbar();
    }

    private void HandleHistoryButtonClick(object sender, RoutedEventArgs e)
    {
        var items = new List<HistoryEntryViewModel>();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Избранное — сначала
        foreach (var key in _favoriteTabs)
        {
            var caption = ResolveHistoryCaption(key);
            if (!string.IsNullOrWhiteSpace(caption) && added.Add(key))
            {
                items.Add(new HistoryEntryViewModel(key, caption, ""));
            }
        }

        // 2. История посещений в обратном порядке (свежие сверху)
        for (var i = _navHistory.Count - 1; i >= 0; i--)
        {
            var key = _navHistory[i];
            if (!added.Add(key))
            {
                continue;
            }
            var caption = ResolveHistoryCaption(key);
            if (!string.IsNullOrWhiteSpace(caption))
            {
                items.Add(new HistoryEntryViewModel(key, caption, ""));
            }
        }

        // 3. Доступные разделы которые пользователь ещё не посещал — снизу
        foreach (var section in _sections.Values)
        {
            if (added.Add(section.Key))
            {
                items.Add(new HistoryEntryViewModel(section.Key, section.Caption, ""));
            }
        }

        HistoryItemsControl.ItemsSource = items;
        HistoryEmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPopup.IsOpen = true;
    }

    private string ResolveHistoryCaption(string key)
    {
        if (_tabsByKey.TryGetValue(key, out var tab))
        {
            var caption = ResolveTabCaption(tab);
            if (!string.IsNullOrWhiteSpace(caption))
            {
                return caption;
            }
        }
        if (_dynamicTabsByKey.TryGetValue(key, out var dynamic))
        {
            return dynamic.Caption;
        }
        if (_sections.TryGetValue(key, out var section))
        {
            return section.Caption;
        }
        return key;
    }

    private void HandleHistoryItemClick(object sender, RoutedEventArgs e)
    {
        HistoryPopup.IsOpen = false;
        if (sender is WpfButton button && button.Tag is string key)
        {
            if (_tabsByKey.ContainsKey(key))
            {
                // tab уже открыт — просто переключусь
                if (_tabsByKey[key] is TabItem tab)
                {
                    WorkspaceTabs.SelectedItem = tab;
                }
            }
            else if (_sections.ContainsKey(key))
            {
                OpenSection(key);
            }
        }
    }

    private sealed record HistoryEntryViewModel(string Key, string Caption, string Glyph);

    // ===== Глобальный поиск =====

    private void HandleGlobalSearchKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (sender is not TextBox box)
        {
            return;
        }

        GlobalSearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(box.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        GlobalSearchClearButton.Visibility = string.IsNullOrWhiteSpace(box.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (e.Key == System.Windows.Input.Key.Escape)
        {
            box.Text = string.Empty;
            GlobalSearchPlaceholder.Visibility = Visibility.Visible;
            GlobalSearchClearButton.Visibility = Visibility.Collapsed;
            e.Handled = true;
            return;
        }

        if (e.Key == System.Windows.Input.Key.Enter)
        {
            var query = box.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                ExecuteGlobalSearch(query);
            }
            e.Handled = true;
        }
    }

    private void HandleGlobalSearchClearClick(object sender, RoutedEventArgs e)
    {
        GlobalSearchBox.Text = string.Empty;
        GlobalSearchPlaceholder.Visibility = Visibility.Visible;
        GlobalSearchClearButton.Visibility = Visibility.Collapsed;
        GlobalSearchBox.Focus();
    }

    private void ExecuteGlobalSearch(string query)
    {
        // Простое поведение: открываем раздел Заказы и переносим запрос в поле поиска.
        // В будущем можно сделать настоящий полнотекстовый по всем разделам.
        if (_sections.ContainsKey("sales"))
        {
            OpenSection("sales");
        }

        // Найти RecordsWorkspaceView в текущей вкладке и передать поисковую строку
        if (WorkspaceTabs.SelectedItem is TabItem tab && tab.Content is FrameworkElement content)
        {
            var searchBox = FindDescendant<TextBox>(content, "HeaderSearchBox");
            if (searchBox is not null)
            {
                searchBox.Text = query;
                searchBox.Focus();
                searchBox.CaretIndex = query.Length;
            }
        }
    }

    private static T? FindDescendant<T>(DependencyObject? root, string? name = null) where T : DependencyObject
    {
        if (root is null)
        {
            return null;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T match)
            {
                if (name is null || (child is FrameworkElement fe && string.Equals(fe.Name, name, StringComparison.Ordinal)))
                {
                    return match;
                }
            }
            var deeper = FindDescendant<T>(child, name);
            if (deeper is not null)
            {
                return deeper;
            }
        }

        return null;
    }

    private void HandleCloseTabClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is WpfButton button && button.Tag is string sectionKey)
        {
            CloseSection(sectionKey);
        }
    }

    private async void HandleUpdateApplicationClick(object sender, RoutedEventArgs e)
    {
        if (_updateOperationInProgress)
        {
            return;
        }

        if (_availableRelease is null)
        {
            await RefreshUpdateStateAsync(showDialogOnNonUpdateResult: true);
            return;
        }

        var confirmation = MessageBox.Show(
            this,
            $"Установить версию {_availableRelease.Version}?{Environment.NewLine}{Environment.NewLine}Приложение закроется, обновит файлы и запустится заново.",
            AppBranding.MessageBoxTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        _updateOperationInProgress = true;
        try
        {
            SetUpdatePanelState(
                $"Установить {_availableRelease.Version}",
                "Скачиваю пакет обновления и готовлю установку...",
                false);

            var launchResult = await _applicationUpdateService.PrepareAndLaunchUpdateAsync(_availableRelease);
            if (!launchResult.IsSuccess)
            {
                SetUpdatePanelState("Проверить обновления", launchResult.Message, true);
                MessageBox.Show(
                    this,
                    launchResult.Message,
                    AppBranding.MessageBoxTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            ApplicationUpdateStatusText.Text = launchResult.Message;
            Close();
        }
        finally
        {
            _updateOperationInProgress = false;
        }
    }

    private sealed record SectionDefinition(
        string Key,
        string Caption,
        string Subtitle,
        bool Closable,
        Func<Control> Factory);

    private sealed record DynamicTabDefinition(
        string Caption,
        string Subtitle);
}

internal sealed record MainWindowStartupData(
    SalesWorkspaceStore SalesWorkspaceStore,
    SalesWorkspace SalesWorkspace,
    bool LoadRemoteWorkspaceAfterStartup);
