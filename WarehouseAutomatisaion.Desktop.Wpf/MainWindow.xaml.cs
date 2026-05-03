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
    private static readonly WpfBrush ActiveNavBackground = BrushFromHex("#EEF2FF");
    private static readonly WpfBrush ActiveNavBorder = BrushFromHex("#C9D3F7");
    private static readonly WpfBrush ActiveNavForeground = BrushFromHex("#2F45D3");

    private static readonly WpfBrush DefaultNavBackground = WpfBrushes.Transparent;
    private static readonly WpfBrush DefaultNavBorder = BrushFromHex("#E3E8F2");
    private static readonly WpfBrush DefaultNavForeground = BrushFromHex("#1B2740");

    private readonly Dictionary<string, SectionDefinition> _sections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TabItem> _tabsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WpfButton> _navButtonsByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DynamicTabDefinition> _dynamicTabsByKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly DemoWorkspace _demoWorkspace;
    private readonly DesktopClientStartupResult _startupStatus;
    private readonly SalesWorkspaceStore _salesWorkspaceStore;
    private readonly SalesWorkspace _salesWorkspace;
    private readonly FunctionalCoverageSnapshot _coverage;
    private readonly ApplicationUpdateService _applicationUpdateService;
    private readonly DispatcherTimer _salesWorkspaceAutosaveTimer;
    private readonly DispatcherTimer _remoteSalesRefreshTimer;

    private ApplicationRelease? _availableRelease;
    private bool _updateOperationInProgress;
    private bool _remoteSalesRefreshInProgress;
    private bool _applyingRemoteSalesRefresh;
    private bool _salesWorkspaceSaveWarningShown;

    public MainWindow(DesktopClientStartupResult startupStatus)
    {
        _startupStatus = startupStatus;
        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);
        EnsureWindowFitsWorkArea();
        Title = AppBranding.ProductName;
        ApplicationNameText.Text = AppBranding.ProductName;
        ApplicationCardTitleText.Text = AppBranding.ProductName;

        _demoWorkspace = DemoWorkspace.Create();
        _salesWorkspaceStore = SalesWorkspaceStore.CreateDefault();
        _salesWorkspace = TryLoadSalesWorkspace(_salesWorkspaceStore, _demoWorkspace.CurrentOperator);
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        _coverage = FunctionalCoverageSnapshot.Create();
        _applicationUpdateService = new ApplicationUpdateService();
        _salesWorkspaceAutosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(750)
        };
        _salesWorkspaceAutosaveTimer.Tick += HandleSalesWorkspaceAutosaveTimerTick;
        _remoteSalesRefreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(15)
        };
        _remoteSalesRefreshTimer.Tick += HandleRemoteSalesRefreshTimerTick;

        RegisterSidebarButtons();
        RegisterSections();
        InitializeDatabaseStatus();
        InitializeUpdatePanel();
        OpenSection("dashboard");

        Loaded += HandleWindowLoaded;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        Loaded -= HandleWindowLoaded;
        _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
        _salesWorkspaceAutosaveTimer.Stop();
        _salesWorkspaceAutosaveTimer.Tick -= HandleSalesWorkspaceAutosaveTimerTick;
        _remoteSalesRefreshTimer.Stop();
        _remoteSalesRefreshTimer.Tick -= HandleRemoteSalesRefreshTimerTick;

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
            MessageBox.Show(
                this,
                $"Не удалось сохранить изменения при закрытии приложения.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            _applicationUpdateService.Dispose();
        }
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_applyingRemoteSalesRefresh)
        {
            return;
        }

        _salesWorkspaceAutosaveTimer.Stop();
        _salesWorkspaceAutosaveTimer.Start();
    }

    private void HandleSalesWorkspaceAutosaveTimerTick(object? sender, EventArgs e)
    {
        _salesWorkspaceAutosaveTimer.Stop();
        TrySaveSalesWorkspace(showWarning: false);
    }

    private bool TrySaveSalesWorkspace(bool showWarning)
    {
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

            RefreshSalesWorkspaceFromServer(showStatus: true);
        }
        catch
        {
        }
        finally
        {
            _remoteSalesRefreshInProgress = false;
        }
    }

    private void RefreshSalesWorkspaceFromServer(bool showStatus)
    {
        _applyingRemoteSalesRefresh = true;
        try
        {
            if (_salesWorkspaceStore.TryRefreshFromBackplane(_salesWorkspace) && showStatus)
            {
                ApplicationUpdateStatusText.Text = "Данные заказов обновлены из общей базы.";
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
        }

        await RefreshUpdateStateAsync(showDialogOnNonUpdateResult: false);
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
        _navButtonsByKey["dashboard"] = NavDashboardButton;
        _navButtonsByKey["sales"] = NavSalesButton;
        _navButtonsByKey["customers"] = NavCustomersButton;
        _navButtonsByKey["invoices"] = NavInvoicesButton;
        _navButtonsByKey["shipments"] = NavShipmentsButton;
        _navButtonsByKey["purchasing"] = NavPurchasingButton;
        _navButtonsByKey["warehouse"] = NavWarehouseButton;
        _navButtonsByKey["catalog"] = NavCatalogButton;
        _navButtonsByKey["audit"] = NavAuditButton;
        _navButtonsByKey["model"] = NavModelButton;
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
            Caption: "Заказы",
            Subtitle: "Управление заказами клиентов в едином рабочем контуре.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateSales(_salesWorkspace)));

        _sections["customers"] = new SectionDefinition(
            Key: "customers",
            Caption: "Клиенты",
            Subtitle: "База клиентов, контактов и статусов работы.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateCustomers(_salesWorkspace)));

        _sections["invoices"] = new SectionDefinition(
            Key: "invoices",
            Caption: "Счета",
            Subtitle: "Выставление и контроль оплаты счетов.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateInvoices(_salesWorkspace)));

        _sections["shipments"] = new SectionDefinition(
            Key: "shipments",
            Caption: "Отгрузки",
            Subtitle: "Исполнение, доставка и контроль статусов отгрузок.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateShipments(_salesWorkspace)));

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

        _sections["model"] = new SectionDefinition(
            Key: "model",
            Caption: "Связи данных",
            Subtitle: "Сценарии, связи и проверка целостности данных.",
            Closable: true,
            Factory: () => new RecordsWorkspaceView(RecordsWorkspaceCatalog.CreateModel(_coverage, _salesWorkspace)));
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

    private void OpenSection(string sectionKey)
    {
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

        WorkspaceTabs.SelectedItem = tab;
        ApplySelection(sectionKey);
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
            ApplySelection(key);
            return true;
        }

        var content = contentFactory();
        WpfTextNormalizer.NormalizeTree(content);
        content.Loaded += (_, _) => WpfTextNormalizer.NormalizeTree(content);

        var tab = new TabItem
        {
            Tag = key,
            Header = CreateTabHeader(key, caption, closable: true),
            Content = content
        };
        System.Windows.Automation.AutomationProperties.SetName(tab, caption);

        _dynamicTabsByKey[key] = new DynamicTabDefinition(caption, subtitle);
        _tabsByKey[key] = tab;
        WorkspaceTabs.Items.Add(tab);
        WorkspaceTabs.SelectedItem = tab;
        ApplySelection(key);
        return true;
    }

    internal void CloseWorkspaceTab(string key)
    {
        CloseSection(key);
    }

    private object CreateSectionContent(SectionDefinition section)
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

        if (!_tabsByKey.TryGetValue(sectionKey, out var tab))
        {
            return;
        }

        if (tab.Content is IDisposable disposable)
        {
            disposable.Dispose();
        }

        WorkspaceTabs.Items.Remove(tab);
        _tabsByKey.Remove(sectionKey);
        _dynamicTabsByKey.Remove(sectionKey);

        if (WorkspaceTabs.Items.Count == 0)
        {
            OpenSection("dashboard");
            return;
        }

        if (WorkspaceTabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is string selectedKey)
        {
            ApplySelection(selectedKey);
        }
        else
        {
            OpenSection("dashboard");
        }
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

        foreach (var pair in _navButtonsByKey)
        {
            var active = pair.Key.Equals(sectionKey, StringComparison.OrdinalIgnoreCase);
            var button = pair.Value;
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
            OpenSection(sectionKey);
        }
    }

    private void HandleWorkspaceTabsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WorkspaceTabs.SelectedItem is TabItem selectedTab && selectedTab.Tag is string sectionKey)
        {
            ApplySelection(sectionKey);
        }
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
