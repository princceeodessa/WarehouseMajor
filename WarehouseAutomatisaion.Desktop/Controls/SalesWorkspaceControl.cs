using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class SalesWorkspaceControl : UserControl
{
    private const string AllOrderStatusesFilter = "Все статусы";

    private readonly DemoWorkspace _demoWorkspace;
    private readonly SalesWorkspace _workspace;
    private readonly SalesCustomersTabControl _customersTabControl;
    private readonly SalesInvoicesTabControl _invoicesTabControl;
    private readonly SalesShipmentsTabControl _shipmentsTabControl;
    private readonly OneCImportSnapshot? _oneCImportSnapshot;
    private readonly BindingSource _customersBindingSource = new();
    private readonly BindingSource _customerOrdersBindingSource = new();
    private readonly BindingSource _ordersBindingSource = new();
    private readonly BindingSource _orderLinesBindingSource = new();
    private readonly BindingSource _incomingOrdersBindingSource = new();
    private readonly BindingSource _operationsBindingSource = new();
    private readonly BindingSource _recentShipmentsBindingSource = new();
    private readonly TextBox _customerSearchTextBox = new();
    private readonly TextBox _orderSearchTextBox = new();
    private readonly TextBox _workspaceSearchTextBox = new();
    private readonly DateTimePicker _ordersDateFromPicker = new();
    private readonly DateTimePicker _ordersDateToPicker = new();
    private readonly ComboBox _orderStatusFilterComboBox = new();
    private readonly Label _activeCustomersValueLabel = new();
    private readonly Label _ordersInWorkValueLabel = new();
    private readonly Label _pipelineValueLabel = new();
    private readonly Label _reserveValueLabel = new();
    private readonly Label _customerNameValueLabel = new();
    private readonly Label _customerContractValueLabel = new();
    private readonly Label _customerManagerValueLabel = new();
    private readonly Label _customerPhoneValueLabel = new();
    private readonly Label _customerEmailValueLabel = new();
    private readonly Label _customerNotesValueLabel = new();
    private readonly Label _orderNumberValueLabel = new();
    private readonly Label _orderCustomerValueLabel = new();
    private readonly Label _orderWarehouseValueLabel = new();
    private readonly Label _orderStatusValueLabel = new();
    private readonly Label _orderManagerValueLabel = new();
    private readonly Label _orderTotalValueLabel = new();
    private readonly Label _orderSupplyValueLabel = new();
    private readonly Label _orderWarehouseActionsValueLabel = new();
    private readonly Label _orderCommentValueLabel = new();
    private readonly Label _customersFilteredLabel = new();
    private readonly Label _ordersFilteredLabel = new();
    private readonly Label _incomingOrdersFilteredLabel = new();
    private readonly System.Windows.Forms.Timer _customerSearchDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _orderSearchDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _refreshDebounceTimer = new();
    private readonly ContextMenuStrip _salesShiftMenu = new();
    private readonly DataGridView _customersGrid;
    private readonly DataGridView _customerOrdersGrid;
    private readonly DataGridView _ordersGrid;
    private readonly DataGridView _orderLinesGrid;
    private readonly DataGridView _incomingOrdersGrid;
    private readonly DataGridView _operationsGrid;
    private readonly DataGridView _recentShipmentsGrid;
    private readonly FlowLayoutPanel _ordersStatusLegendPanel = new();
    private readonly Panel _ordersStatusDonutPanel = new();
    private readonly Label _ordersStatusTotalLabel = new();
    private Control? _oneCImportControl;
    private TabPage? _oneCImportTabPage;
    private Control? _workspaceDashboardHost;
    private Panel? _workspaceTabHost;
    private Control? _workspaceProcessTabsHost;
    private StyledTabControl? _mainTabsControl;
    private StyledTabControl? _workspaceProcessTabs;
    private TabPage? _workspaceCustomersTab;
    private TabPage? _workspaceOrdersTab;
    private IReadOnlyList<OrderStatusBreakdownSlice> _ordersStatusSlices = Array.Empty<OrderStatusBreakdownSlice>();
    private bool _oneCImportLoadQueued;
    private bool _refreshPendingWhileHidden;
    private bool _syncingOrderSelection;

    public SalesWorkspaceControl(DemoWorkspace demoWorkspace, SalesWorkspace workspace)
    {
        _demoWorkspace = demoWorkspace;
        _workspace = workspace;
        _customersTabControl = new SalesCustomersTabControl(workspace);
        _invoicesTabControl = new SalesInvoicesTabControl(workspace);
        _shipmentsTabControl = new SalesShipmentsTabControl(workspace);
        _oneCImportSnapshot = workspace.OneCImport?.HasAnyData == true
            ? workspace.OneCImport
            : null;
        _oneCImportControl = _oneCImportSnapshot is null
            ? null
            : CreateOneCImportPlaceholder();
        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        _customersGrid = DesktopGridFactory.CreateGrid(Array.Empty<CustomerGridRow>());
        _customerOrdersGrid = DesktopGridFactory.CreateGrid(Array.Empty<CustomerOrderRow>());
        _ordersGrid = DesktopGridFactory.CreateGrid(Array.Empty<OrderGridRow>());
        _orderLinesGrid = DesktopGridFactory.CreateGrid(Array.Empty<OrderLineGridRow>());
        _incomingOrdersGrid = DesktopGridFactory.CreateGrid(Array.Empty<IncomingOrderRow>());
        _operationsGrid = DesktopGridFactory.CreateGrid(Array.Empty<OperationLogRow>());
        _recentShipmentsGrid = DesktopGridFactory.CreateGrid(Array.Empty<RecentShipmentRow>());
        _customersGrid.CellFormatting += HandleStatusCellFormatting;
        _customerOrdersGrid.CellFormatting += HandleStatusCellFormatting;
        _ordersGrid.CellFormatting += HandleStatusCellFormatting;
        _incomingOrdersGrid.CellFormatting += HandleStatusCellFormatting;
        _recentShipmentsGrid.CellFormatting += HandleStatusCellFormatting;
        _ordersStatusDonutPanel.Width = 170;
        _ordersStatusDonutPanel.Height = 170;
        _ordersStatusDonutPanel.Margin = new Padding(0);
        _ordersStatusDonutPanel.BackColor = DesktopTheme.Surface;
        _ordersStatusDonutPanel.Paint += HandleOrdersStatusDonutPaint;
        _ordersStatusDonutPanel.Resize += (_, _) => LayoutOrdersDonutLabel();
        _ordersStatusTotalLabel.Dock = DockStyle.Fill;
        _ordersStatusTotalLabel.Font = DesktopTheme.TitleFont(18f);
        _ordersStatusTotalLabel.ForeColor = DesktopTheme.TextPrimary;
        _ordersStatusTotalLabel.TextAlign = ContentAlignment.MiddleCenter;
        _ordersDateFromPicker.Format = DateTimePickerFormat.Custom;
        _ordersDateFromPicker.CustomFormat = "dd.MM.yyyy";
        _ordersDateToPicker.Format = DateTimePickerFormat.Custom;
        _ordersDateToPicker.CustomFormat = "dd.MM.yyyy";
        _ordersDateFromPicker.Width = 118;
        _ordersDateToPicker.Width = 118;
        var defaultToDate = _workspace.Orders.Count > 0
            ? _workspace.Orders.Max(order => order.OrderDate).Date
            : DateTime.Today;
        var defaultFromDate = _workspace.Orders.Count > 0
            ? _workspace.Orders.Min(order => order.OrderDate).Date
            : defaultToDate.AddDays(-30);
        if ((defaultToDate - defaultFromDate).TotalDays > 45)
        {
            defaultFromDate = defaultToDate.AddDays(-30);
        }

        _ordersDateFromPicker.Value = defaultFromDate;
        _ordersDateToPicker.Value = defaultToDate;
        _ordersDateFromPicker.ValueChanged += (_, _) => RefreshOrders();
        _ordersDateToPicker.ValueChanged += (_, _) => RefreshOrders();
        _customerSearchDebounceTimer.Interval = 180;
        _customerSearchDebounceTimer.Tick += HandleCustomerSearchDebounceTick;
        _orderSearchDebounceTimer.Interval = 180;
        _orderSearchDebounceTimer.Tick += HandleOrderSearchDebounceTick;
        _refreshDebounceTimer.Interval = 120;
        _refreshDebounceTimer.Tick += HandleRefreshDebounceTick;
        ConfigureOrderStatusFilterComboBox();

        BuildLayout();
        TextMojibakeFixer.NormalizeControlTree(this);
        WireBindings();
        RefreshAll();

        _workspace.Changed += HandleWorkspaceChanged;
        VisibleChanged += HandleVisibilityChanged;
        Disposed += (_, _) =>
        {
            _workspace.Changed -= HandleWorkspaceChanged;
            VisibleChanged -= HandleVisibilityChanged;
            _customersTabControl.NavigateRequested -= HandleWorkspaceNavigationRequested;
            _invoicesTabControl.NavigateRequested -= HandleWorkspaceNavigationRequested;
            _shipmentsTabControl.NavigateRequested -= HandleWorkspaceNavigationRequested;
            _customerSearchDebounceTimer.Stop();
            _customerSearchDebounceTimer.Tick -= HandleCustomerSearchDebounceTick;
            _customerSearchDebounceTimer.Dispose();
            _orderSearchDebounceTimer.Stop();
            _orderSearchDebounceTimer.Tick -= HandleOrderSearchDebounceTick;
            _orderSearchDebounceTimer.Dispose();
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Tick -= HandleRefreshDebounceTick;
            _refreshDebounceTimer.Dispose();
            _salesShiftMenu.Dispose();
            _ordersStatusDonutPanel.Paint -= HandleOrdersStatusDonutPaint;
        };
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => HandleWorkspaceChanged(sender, e));
            return;
        }

        if (!CanRefreshNow())
        {
            _refreshPendingWhileHidden = true;
            return;
        }

        ScheduleRefresh();
    }

    private void HandleVisibilityChanged(object? sender, EventArgs e)
    {
        if (!CanRefreshNow())
        {
            return;
        }

        if (!_refreshPendingWhileHidden)
        {
            return;
        }

        _refreshPendingWhileHidden = false;
        _refreshDebounceTimer.Stop();
        RefreshAll();
    }

    private bool CanRefreshNow()
    {
        return !IsDisposed && IsHandleCreated && Visible && Parent is not null;
    }

    private void ScheduleRefresh()
    {
        _refreshDebounceTimer.Stop();
        _refreshDebounceTimer.Start();
    }

    private void HandleRefreshDebounceTick(object? sender, EventArgs e)
    {
        _refreshDebounceTimer.Stop();
        if (!CanRefreshNow())
        {
            _refreshPendingWhileHidden = true;
            return;
        }

        RefreshAll();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateTabs(), 0, 0);

        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 66,
            Padding = new Padding(0, 0, 0, 8)
        };

        panel.Controls.Add(new Label
        {
            Text = "Рабочее место менеджера: клиенты, заказы, счета и отгрузки без меню 1С.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });

        panel.Controls.Add(new Label
        {
            Text = "Операционные продажи",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        return panel;
    }

    private Control CreateSummaryCards()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 12)
        };

        flow.Controls.Add(CreateSummaryCard("Активные клиенты", "Кого менеджеры реально ведут сейчас.", _activeCustomersValueLabel, Color.FromArgb(79, 174, 92)));
        flow.Controls.Add(CreateSummaryCard("Заказы в работе", "Все незавершенные продажи.", _ordersInWorkValueLabel, Color.FromArgb(78, 160, 190)));
        flow.Controls.Add(CreateSummaryCard("Воронка", "Сумма текущих заказов.", _pipelineValueLabel, Color.FromArgb(201, 134, 64)));
        flow.Controls.Add(CreateSummaryCard("В резерве", "Что уже обещано клиенту.", _reserveValueLabel, Color.FromArgb(123, 104, 163)));
        return flow;
    }

    private Control CreateOperatorQuickActions()
    {
        var shell = DesktopSurfaceFactory.CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.AutoSize = true;
        shell.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        shell.MinimumSize = new Size(0, 68);
        shell.Padding = new Padding(12, 10, 12, 10);
        shell.Margin = new Padding(0, 0, 0, 12);
        shell.BackColor = DesktopTheme.SurfaceAlt;

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            AutoScroll = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0)
        };

        ConfigureSalesShiftMenu();
        var shiftButton = DesktopSurfaceFactory.CreateActionButton("Смена продаж ▾", (_, _) => { }, DesktopButtonTone.Ghost, new Padding(0, 0, 8, 8));
        shiftButton.AutoSize = false;
        shiftButton.Width = 138;
        shiftButton.Height = 34;
        shiftButton.Click += (_, _) => _salesShiftMenu.Show(shiftButton, new Point(0, shiftButton.Height));
        flow.Controls.Add(shiftButton);

        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый заказ", (_, _) => CreateOrder(), DesktopButtonTone.Primary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый покупатель", (_, _) => CreateCustomer(), DesktopButtonTone.Secondary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Счет из заказа", (_, _) => CreateInvoiceFromSelectedOrder(), DesktopButtonTone.Secondary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Отгрузка", (_, _) => CreateShipmentFromSelectedOrder(), DesktopButtonTone.Ghost));

        shell.Controls.Add(flow);
        return shell;
    }

    private void ConfigureSalesShiftMenu()
    {
        if (_salesShiftMenu.Items.Count > 0)
        {
            return;
        }

        _salesShiftMenu.ShowImageMargin = false;
        _salesShiftMenu.Font = DesktopTheme.BodyFont(9.2f);
        _salesShiftMenu.Items.Add("Открыть смену", null, (_, _) => ShowSelectionWarning("Смена открыта. Можно работать по заказам и отгрузке."));
        _salesShiftMenu.Items.Add("Передать смену", null, (_, _) => ShowSelectionWarning("Передача смены выполнена. Проверьте очередь задач и ответственных."));
        _salesShiftMenu.Items.Add("Закрыть смену", null, (_, _) => ShowSelectionWarning("Смена закрыта. Итоги зафиксированы в журнале операций."));
    }

    private static Control CreateSummaryCard(string title, string hint, Label valueLabel, Color accentColor)
    {
        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 42;
        valueLabel.Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(43, 39, 34);

        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Width = 284;
        panel.Height = 112;
        panel.Margin = new Padding(0, 0, 12, 12);
        panel.Padding = new Padding(16, 14, 16, 14);

        var accent = new Panel
        {
            Dock = DockStyle.Left,
            Width = 5,
            BackColor = accentColor
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 0, 0)
        };
        content.Controls.Add(new Label
        {
            Text = hint,
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(112, 103, 92)
        });
        content.Controls.Add(valueLabel);
        content.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(68, 61, 53)
        });

        panel.Controls.Add(content);
        panel.Controls.Add(accent);
        return panel;
    }

    private Control CreateTabs()
    {
        var tabs = DesktopSurfaceFactory.CreateTabControl();
        _mainTabsControl = tabs;
        tabs.SelectedIndexChanged += (_, _) => EnsureDeferredTabsLoaded(tabs);

        tabs.TabPages.Add(CreateWorkspaceTab());
        tabs.TabPages.Add(CreateHostedTabPage("Клиенты", _customersTabControl));
        tabs.TabPages.Add(CreateReadonlyTab("Счета", "Список счетов на оплату с контролем выставления и оплаты.", _demoWorkspace.SalesInvoices));
        tabs.TabPages.Add(CreateReadonlyTab("Отгрузки", "Расходные накладные и готовность к отгрузке по продажам.", _demoWorkspace.SalesShipments));
        tabs.TabPages.Add(CreateOperationsTab());
        if (_oneCImportSnapshot is not null)
        {
            tabs.TabPages.Add(CreateHostedTabPage("Данные 1С", _oneCImportControl!));
        }

        return tabs;
    }

    private TabPage CreateWorkspaceTab()
    {
        var tab = new TabPage("Заказы")
        {
            Padding = new Padding(8)
        };

        _workspaceSearchTextBox.Width = 280;
        _workspaceSearchTextBox.Font = DesktopTheme.BodyFont(10f);
        _workspaceSearchTextBox.PlaceholderText = "Поиск по заказам и клиентам";
        _workspaceSearchTextBox.TextChanged -= HandleWorkspaceSearchChanged;
        _workspaceSearchTextBox.TextChanged += HandleWorkspaceSearchChanged;

        var root = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0),
            Margin = new Padding(0)
        };

        _workspaceTabHost = root;
        _workspaceDashboardHost = CreateWorkspaceDashboard();
        _workspaceProcessTabsHost = CreateWorkspaceProcessTabsHost();
        _workspaceProcessTabsHost.Visible = false;

        _workspaceDashboardHost.Dock = DockStyle.Fill;
        _workspaceProcessTabsHost.Dock = DockStyle.Fill;
        root.Controls.Add(_workspaceProcessTabsHost);
        root.Controls.Add(_workspaceDashboardHost);
        tab.Controls.Add(root);
        ShowWorkspaceDashboard();
        return tab;
    }

    private Control CreateWorkspaceDashboard()
    {
        var canvas = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(246, 248, 253),
            Padding = new Padding(12, 10, 12, 12)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateOrdersDashboardHeader(), 0, 0);
        root.Controls.Add(CreateOrdersMetricsStrip(), 0, 1);
        root.Controls.Add(CreateOrdersNavigationStrip(), 0, 2);
        root.Controls.Add(CreateOrdersMainListShell(), 0, 3);
        root.Controls.Add(CreateOrdersInsightsStrip(), 0, 4);
        root.Controls.Add(CreateOrdersQuickActionsShell(), 0, 5);

        void SyncDashboardWidth()
        {
            root.Width = Math.Max(980, canvas.ClientSize.Width - canvas.Padding.Horizontal);
        }

        canvas.Controls.Add(root);
        canvas.Resize += (_, _) => SyncDashboardWidth();
        SyncDashboardWidth();
        return canvas;
    }

    private Control CreateOrdersDashboardHeader()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var left = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 74,
            Margin = new Padding(0)
        };
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Font = DesktopTheme.BodyFont(9.8f),
            ForeColor = Color.FromArgb(106, 118, 142),
            Text = "Быстрая обработка заказов, счетов и отгрузок."
        });
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 46,
            Font = DesktopTheme.TitleFont(28f),
            ForeColor = Color.FromArgb(20, 33, 61),
            Text = "Заказы"
        });

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        _workspaceSearchTextBox.Width = 292;
        _workspaceSearchTextBox.Height = 32;
        _workspaceSearchTextBox.Margin = new Padding(0, 0, 0, 6);
        _workspaceSearchTextBox.BorderStyle = BorderStyle.FixedSingle;
        _workspaceSearchTextBox.BackColor = Color.White;
        _workspaceSearchTextBox.ForeColor = Color.FromArgb(67, 79, 104);
        _workspaceSearchTextBox.TextAlign = HorizontalAlignment.Left;
        _workspaceSearchTextBox.Font = DesktopTheme.BodyFont(10f);
        _workspaceSearchTextBox.PlaceholderText = "Поиск...";

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 2),
            Margin = new Padding(0)
        };
        searchRow.Controls.Add(_workspaceSearchTextBox);

        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        actionsRow.Controls.Add(CreateDashboardHeaderButton("Новый заказ", true, (_, _) => CreateOrder(), new Size(136, 34)));
        actionsRow.Controls.Add(CreateDashboardHeaderButton("Импорт", false, (_, _) => QueueOneCImportLoad(), new Size(96, 34)));

        right.Controls.Add(searchRow, 0, 0);
        right.Controls.Add(actionsRow, 0, 1);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        return root;
    }

    private static Button CreateDashboardHeaderButton(string text, bool primary, EventHandler handler, Size size)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = false,
            Size = size,
            FlatStyle = FlatStyle.Flat,
            Font = DesktopTheme.EmphasisFont(9.2f),
            Margin = new Padding(8, 0, 0, 0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            BackColor = primary ? Color.FromArgb(84, 97, 245) : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(60, 74, 105)
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = primary ? Color.FromArgb(84, 97, 245) : Color.FromArgb(219, 225, 238);
        button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(72, 84, 227) : Color.FromArgb(247, 249, 255);
        button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(66, 77, 209) : Color.FromArgb(240, 244, 253);
        button.Click += handler;
        return button;
    }

    private Control CreateOrdersMetricsStrip()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 10),
            Margin = new Padding(0)
        };

        panel.Controls.Add(CreateOrdersMetricCard("Заказы", _ordersInWorkValueLabel, "+12%", "к прошлому месяцу", Color.FromArgb(90, 97, 235)));
        panel.Controls.Add(CreateOrdersMetricCard("Счета", _pipelineValueLabel, "+8%", "к прошлому месяцу", Color.FromArgb(100, 183, 121)));
        panel.Controls.Add(CreateOrdersMetricCard("Отгрузки", _reserveValueLabel, "+15%", "к прошлому месяцу", Color.FromArgb(88, 131, 224)));
        panel.Controls.Add(CreateOrdersMetricCard("Клиенты", _activeCustomersValueLabel, "+5%", "к прошлому месяцу", Color.FromArgb(227, 161, 65)));
        return panel;
    }

    private static Control CreateOrdersMetricCard(string title, Label valueLabel, string trend, string trendHint, Color accent)
    {
        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 34;
        valueLabel.Font = DesktopTheme.TitleFont(22f);
        valueLabel.ForeColor = Color.FromArgb(20, 33, 61);
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var card = CreateCardShell();
        card.Dock = DockStyle.None;
        card.Width = 198;
        card.Height = 128;
        card.Margin = new Padding(0, 0, 10, 10);
        card.Padding = new Padding(12, 12, 12, 10);
        card.BackColor = Color.White;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var caption = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 6)
        };
        caption.Controls.Add(new RoundedSurfacePanel
        {
            Width = 28,
            Height = 28,
            BackColor = Color.FromArgb(44, accent),
            BorderColor = Color.FromArgb(65, accent),
            BorderThickness = 0,
            CornerRadius = 9,
            DrawShadow = false,
            Margin = new Padding(0, 0, 8, 0)
        });
        caption.Controls.Add(new Label
        {
            AutoSize = true,
            Font = DesktopTheme.EmphasisFont(9.6f),
            ForeColor = Color.FromArgb(51, 65, 95),
            Text = title,
            Margin = new Padding(0, 5, 0, 0)
        });

        root.Controls.Add(caption, 0, 0);
        root.Controls.Add(valueLabel, 0, 1);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = DesktopTheme.EmphasisFont(9.2f),
            ForeColor = Color.FromArgb(48, 166, 99),
            Text = trend,
            Margin = new Padding(0, 0, 0, 0)
        }, 0, 2);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = DesktopTheme.BodyFont(8.8f),
            ForeColor = Color.FromArgb(112, 124, 146),
            Text = trendHint,
            Margin = new Padding(0, 0, 0, 0)
        }, 0, 3);

        card.Controls.Add(root);
        return card;
    }

    private Control CreateOrdersNavigationStrip()
    {
        var container = new Panel
        {
            Dock = DockStyle.Top,
            Height = 36,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };

        container.Controls.Add(new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            BackColor = Color.FromArgb(224, 230, 242)
        });

        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = false,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        panel.Controls.Add(CreateDashboardSectionButton("Заказы", true, (_, _) => ShowWorkspaceDashboard()));
        panel.Controls.Add(CreateDashboardSectionButton("Клиенты", false, (_, _) => NavigateToMainTab("Клиенты")));
        panel.Controls.Add(CreateDashboardSectionButton("Счета", false, (_, _) => NavigateToMainTab("Счета")));
        panel.Controls.Add(CreateDashboardSectionButton("Отгрузки", false, (_, _) => NavigateToMainTab("Отгрузки")));

        container.Controls.Add(panel);
        return container;
    }

    private static Control CreateDashboardSectionButton(string text, bool active, EventHandler handler)
    {
        var host = new Panel
        {
            Width = 104,
            Height = 34,
            Margin = new Padding(0)
        };

        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = active ? DesktopTheme.EmphasisFont(9.4f) : DesktopTheme.BodyFont(9.4f),
            BackColor = Color.Transparent,
            ForeColor = active ? Color.FromArgb(84, 97, 245) : Color.FromArgb(84, 96, 122),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += handler;
        host.Controls.Add(button);

        if (active)
        {
            host.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 2,
                BackColor = Color.FromArgb(84, 97, 245)
            });
        }

        return host;
    }

    private Control CreateOrdersMainListShell()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.Height = 376;
        shell.Padding = new Padding(18, 14, 18, 12);
        shell.Margin = new Padding(0, 0, 0, 12);
        shell.BackColor = Color.White;

        _incomingOrdersGrid.Dock = DockStyle.Fill;
        _incomingOrdersGrid.MinimumSize = new Size(0, 220);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 46,
            Margin = new Padding(0, 0, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        header.Controls.Add(CreateSectionHeader("Список заказов", "Новые заявки и текущие заказы с быстрыми действиями."), 0, 0);
        header.Controls.Add(new Label
        {
            Text = "⚙",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Symbol", 10f, FontStyle.Regular),
            ForeColor = Color.FromArgb(107, 119, 146),
            TextAlign = ContentAlignment.MiddleCenter
        }, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(CreateOrdersListFilters(), 0, 1);
        root.Controls.Add(_incomingOrdersGrid, 0, 2);
        root.Controls.Add(CreateOrdersListFooter(), 0, 3);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateOrdersListFilters()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8),
            Margin = new Padding(0)
        };

        _orderStatusFilterComboBox.Margin = new Padding(0, 0, 10, 0);
        _orderStatusFilterComboBox.Width = 160;
        _orderStatusFilterComboBox.Height = 34;

        _ordersDateFromPicker.Margin = new Padding(4, 4, 2, 0);
        _ordersDateToPicker.Margin = new Padding(2, 4, 4, 0);

        var dateRangeHost = new Panel
        {
            Width = 236,
            Height = 34,
            Margin = new Padding(0, 0, 10, 0),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.White
        };
        var dateRangeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        dateRangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 47));
        dateRangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 12));
        dateRangeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 53));
        dateRangeLayout.Controls.Add(_ordersDateFromPicker, 0, 0);
        dateRangeLayout.Controls.Add(new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(126, 137, 161),
            Font = DesktopTheme.BodyFont(9f)
        }, 1, 0);
        dateRangeLayout.Controls.Add(_ordersDateToPicker, 2, 0);
        dateRangeHost.Controls.Add(dateRangeLayout);

        var filterButton = CreateDashboardHeaderButton("Фильтры", false, (_, _) => ShowSelectionWarning("Гибкие фильтры подключим следующим шагом."), new Size(100, 32));
        filterButton.Margin = new Padding(0, 0, 0, 0);

        panel.Controls.Add(_orderStatusFilterComboBox);
        panel.Controls.Add(dateRangeHost);
        panel.Controls.Add(filterButton);
        return panel;
    }

    private Control CreateOrdersListFooter()
    {
        _incomingOrdersFilteredLabel.AutoSize = true;
        _incomingOrdersFilteredLabel.Margin = new Padding(0, 9, 0, 0);
        _incomingOrdersFilteredLabel.Font = DesktopTheme.BodyFont(9.8f);
        _incomingOrdersFilteredLabel.ForeColor = Color.FromArgb(108, 120, 141);
        _incomingOrdersFilteredLabel.BackColor = Color.Transparent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 36,
            Margin = new Padding(0, 8, 0, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        root.Controls.Add(_incomingOrdersFilteredLabel, 0, 0);

        var pager = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        pager.Controls.Add(CreatePagerButton("?", false));
        pager.Controls.Add(CreatePagerButton("13", false));
        pager.Controls.Add(CreatePagerButton("?", false));
        pager.Controls.Add(CreatePagerButton("3", false));
        pager.Controls.Add(CreatePagerButton("2", false));
        pager.Controls.Add(CreatePagerButton("1", true));
        pager.Controls.Add(CreatePagerButton("?", false));
        root.Controls.Add(pager, 1, 0);
        return root;
    }

    private static Control CreatePagerButton(string text, bool active)
    {
        var button = new Label
        {
            AutoSize = false,
            Width = 28,
            Height = 28,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = active ? DesktopTheme.EmphasisFont(9f) : DesktopTheme.BodyFont(9f),
            ForeColor = active ? Color.FromArgb(84, 97, 245) : Color.FromArgb(100, 113, 140),
            BackColor = active ? Color.FromArgb(240, 242, 255) : Color.FromArgb(250, 251, 254),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 0, 0, 0)
        };
        return button;
    }

    private Control CreateOrdersInsightsStrip()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 248,
            Margin = new Padding(0, 0, 0, 10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 248));

        root.Controls.Add(CreateRecentShipmentsShell(), 0, 0);
        root.Controls.Add(CreateOrdersStatusShell(), 1, 0);
        return root;
    }

    private Control CreateRecentShipmentsShell()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Fill;
        shell.Padding = new Padding(14);
        shell.Margin = new Padding(0, 0, 14, 0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 32,
            Margin = new Padding(0, 0, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        header.Controls.Add(CreateSectionHeader("Последние отгрузки", ""), 0, 0);
        header.Controls.Add(new LinkLabel
        {
            Dock = DockStyle.Fill,
            Text = "Смотреть все",
            LinkColor = Color.FromArgb(84, 97, 245),
            ActiveLinkColor = Color.FromArgb(69, 82, 224),
            VisitedLinkColor = Color.FromArgb(84, 97, 245),
            TextAlign = ContentAlignment.MiddleRight,
            Font = DesktopTheme.BodyFont(9.2f)
        }, 1, 0);
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_recentShipmentsGrid, 0, 1);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateOrdersStatusShell()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Fill;
        shell.Padding = new Padding(14);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 32,
            Margin = new Padding(0, 0, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        header.Controls.Add(CreateSectionHeader("Заказы по статусам", ""), 0, 0);
        header.Controls.Add(new LinkLabel
        {
            Dock = DockStyle.Fill,
            Text = "Смотреть все",
            LinkColor = Color.FromArgb(84, 97, 245),
            ActiveLinkColor = Color.FromArgb(69, 82, 224),
            VisitedLinkColor = Color.FromArgb(84, 97, 245),
            TextAlign = ContentAlignment.MiddleRight,
            Font = DesktopTheme.BodyFont(9.2f)
        }, 1, 0);
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 188));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _ordersStatusDonutPanel.Dock = DockStyle.Fill;
        _ordersStatusTotalLabel.BackColor = DesktopTheme.Surface;
        _ordersStatusDonutPanel.Controls.Add(_ordersStatusTotalLabel);
        LayoutOrdersDonutLabel();

        _ordersStatusLegendPanel.Dock = DockStyle.Fill;
        _ordersStatusLegendPanel.AutoScroll = true;
        _ordersStatusLegendPanel.FlowDirection = FlowDirection.TopDown;
        _ordersStatusLegendPanel.WrapContents = false;
        _ordersStatusLegendPanel.Padding = new Padding(6, 2, 0, 0);

        body.Controls.Add(_ordersStatusDonutPanel, 0, 0);
        body.Controls.Add(_ordersStatusLegendPanel, 1, 0);
        root.Controls.Add(body, 0, 1);

        shell.Controls.Add(root);
        return shell;
    }

    private void LayoutOrdersDonutLabel()
    {
        _ordersStatusTotalLabel.Dock = DockStyle.None;
        _ordersStatusTotalLabel.Size = new Size(90, 56);
        _ordersStatusTotalLabel.Location = new Point(
            Math.Max(0, (_ordersStatusDonutPanel.Width - _ordersStatusTotalLabel.Width) / 2),
            Math.Max(0, (_ordersStatusDonutPanel.Height - _ordersStatusTotalLabel.Height) / 2));
    }

    private Control CreateOrdersQuickActionsShell()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.AutoSize = true;
        shell.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        shell.Padding = new Padding(14);
        shell.Margin = new Padding(0, 0, 0, 6);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(CreateSectionHeader("Быстрые действия", ""), 0, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 0),
            Margin = new Padding(0)
        };
        actions.Controls.Add(CreateQuickActionTile("Новый заказ", "Создать новый заказ", (_, _) => CreateOrder()));
        actions.Controls.Add(CreateQuickActionTile("Новый счет", "Выставить счет клиенту", (_, _) => CreateInvoiceFromSelectedOrder()));
        actions.Controls.Add(CreateQuickActionTile("Новая отгрузка", "Оформить отгрузку", (_, _) => CreateShipmentFromSelectedOrder()));
        actions.Controls.Add(CreateQuickActionTile("Новый клиент", "Добавить карточку клиента", (_, _) => CreateCustomer()));

        root.Controls.Add(actions, 0, 1);
        shell.Controls.Add(root);
        return shell;
    }

    private static Button CreateQuickActionTile(string title, string subtitle, EventHandler handler)
    {
        var button = new Button
        {
            AutoSize = false,
            Width = 220,
            Height = 74,
            FlatStyle = FlatStyle.Flat,
            Font = DesktopTheme.EmphasisFont(9.6f),
            TextAlign = ContentAlignment.MiddleLeft,
            Text = $"{title}{Environment.NewLine}{subtitle}",
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 10, 10),
            BackColor = Color.FromArgb(251, 252, 255),
            ForeColor = Color.FromArgb(30, 44, 76),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(224, 230, 242);
        button.FlatAppearance.BorderSize = 1;
        button.Click += handler;
        return button;
    }

    private void NavigateToMainTab(string captionToken)
    {
        if (_mainTabsControl is null)
        {
            return;
        }

        var target = _mainTabsControl.TabPages
            .Cast<TabPage>()
            .FirstOrDefault(page => TextMojibakeFixer.NormalizeText(page.Text)
                .Contains(captionToken, StringComparison.OrdinalIgnoreCase));
        if (target is not null)
        {
            _mainTabsControl.SelectedTab = target;
        }
    }

    private Control CreateWorkspaceToolbar()
    {
        _workspaceSearchTextBox.Margin = new Padding(0, 2, 8, 8);
        var panel = DesktopSurfaceFactory.CreateToolbarStrip();

        panel.Controls.Add(_workspaceSearchTextBox);
        panel.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый заказ", (_, _) => CreateOrder(), DesktopButtonTone.Primary));
        panel.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый покупатель", (_, _) => CreateCustomer(), DesktopButtonTone.Secondary));
        panel.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Сформировать счет", (_, _) => CreateInvoiceFromSelectedOrder(), DesktopButtonTone.Secondary));
        panel.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Подготовить отгрузку", (_, _) => CreateShipmentFromSelectedOrder(), DesktopButtonTone.Secondary));
        return panel;
    }

    private Control CreateWorkspaceLaunchpad()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.AutoSize = true;
        shell.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        shell.Padding = new Padding(18);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateSectionHeader(
            "Вкладки обработки",
            "Открывайте клиентов и заказы как вкладки прямо в текущем экране."),
            0,
            0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 2, 0, 0)
        };
        buttons.Controls.Add(CreateLaunchpadButton(
            "Открыть клиентов",
            (_, _) => OpenWorkspaceProcessTab(WorkspaceProcessFocus.Customers),
            DesktopButtonTone.Secondary,
            new Padding(0, 0, 10, 10)));
        buttons.Controls.Add(CreateLaunchpadButton(
            "Открыть заказы",
            (_, _) => OpenWorkspaceProcessTab(WorkspaceProcessFocus.Orders),
            DesktopButtonTone.Secondary,
            new Padding(0, 0, 10, 10)));
        buttons.Controls.Add(CreateLaunchpadButton(
            "Открыть все",
            (_, _) => OpenWorkspaceProcessTab(WorkspaceProcessFocus.Full),
            DesktopButtonTone.Primary,
            new Padding(0, 0, 0, 10)));

        root.Controls.Add(buttons, 0, 1);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = DesktopTheme.BodyFont(10f),
            ForeColor = DesktopTheme.TextSecondary,
            TextAlign = ContentAlignment.TopLeft,
            Text = "Вкладки открываются ниже и работают как быстрые рабочие листы без всплывающих окон."
        }, 0, 2);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateWorkspaceProcessTabsHost()
    {
        _workspaceProcessTabs = DesktopSurfaceFactory.CreateTabControl();
        _workspaceProcessTabs.Dock = DockStyle.Fill;
        _workspaceProcessTabs.Visible = false;

        var host = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0)
        };
        host.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        host.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var navBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
        navBar.Controls.Add(DesktopSurfaceFactory.CreateActionButton("← Вернуться к заказам", (_, _) => ShowWorkspaceDashboard(), DesktopButtonTone.Ghost, new Padding(0, 0, 8, 0)));
        navBar.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Клиенты", (_, _) => OpenWorkspaceProcessTab(WorkspaceProcessFocus.Customers), DesktopButtonTone.Secondary, new Padding(0, 0, 8, 0)));
        navBar.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Заказы", (_, _) => OpenWorkspaceProcessTab(WorkspaceProcessFocus.Orders), DesktopButtonTone.Secondary, new Padding(0, 0, 0, 0)));

        host.Controls.Add(navBar, 0, 0);
        host.Controls.Add(_workspaceProcessTabs, 0, 1);
        return host;
    }

    private void OpenWorkspaceProcessTab(WorkspaceProcessFocus focus)
    {
        if (_mainTabsControl is not null && _mainTabsControl.TabPages.Count > 0)
        {
            _mainTabsControl.SelectedIndex = 0;
        }

        EnsureWorkspaceProcessTabsInitialized();
        if (_workspaceProcessTabs is null)
        {
            return;
        }

        if (_workspaceProcessTabs.TabPages.Count == 0)
        {
            return;
        }

        EnsureWorkspaceProcessTabsVisible();
        TextMojibakeFixer.NormalizeControlTree(_workspaceProcessTabs);

        var customersTab = GetProcessTab(WorkspaceProcessFocus.Customers) ?? _workspaceProcessTabs.TabPages[0];
        var ordersTab = GetProcessTab(WorkspaceProcessFocus.Orders)
            ?? (_workspaceProcessTabs.TabPages.Count > 1 ? _workspaceProcessTabs.TabPages[1] : _workspaceProcessTabs.TabPages[0]);

        if (focus == WorkspaceProcessFocus.Full)
        {
            _workspaceProcessTabs.SelectedTab = ordersTab;
            _workspaceProcessTabs.Visible = _workspaceProcessTabs.TabPages.Count > 0;
            _workspaceProcessTabs.BringToFront();
            RefreshCustomers();
            RefreshOrders();
            ApplyWorkspaceProcessFocus(WorkspaceProcessFocus.Orders);
            return;
        }

        var tab = focus == WorkspaceProcessFocus.Customers ? customersTab : ordersTab;
        _workspaceProcessTabs.SelectedTab = tab;
        _workspaceProcessTabs.Visible = true;
        _workspaceProcessTabs.BringToFront();
        RefreshCustomers();
        RefreshOrders();
        ApplyWorkspaceProcessFocus(focus);
    }

    private void EnsureWorkspaceProcessTabsVisible()
    {
        if (_workspaceDashboardHost is not null)
        {
            _workspaceDashboardHost.Visible = false;
        }

        if (_workspaceProcessTabsHost is not null)
        {
            _workspaceProcessTabsHost.Visible = true;
            _workspaceProcessTabsHost.BringToFront();
        }
        _workspaceTabHost?.PerformLayout();
    }

    private void ShowWorkspaceDashboard()
    {
        if (_mainTabsControl is not null && _mainTabsControl.TabPages.Count > 0)
        {
            _mainTabsControl.SelectedIndex = 0;
        }

        if (_workspaceProcessTabsHost is not null)
        {
            _workspaceProcessTabsHost.Visible = false;
        }

        if (_workspaceDashboardHost is not null)
        {
            _workspaceDashboardHost.Visible = true;
            _workspaceDashboardHost.BringToFront();
        }
        _workspaceTabHost?.PerformLayout();
    }

    private void EnsureWorkspaceProcessTabsInitialized()
    {
        if (_workspaceProcessTabs is null)
        {
            return;
        }

        if (_workspaceCustomersTab is null || _workspaceCustomersTab.IsDisposed)
        {
            _workspaceCustomersTab = CreateCustomersTab();
        }

        if (_workspaceOrdersTab is null || _workspaceOrdersTab.IsDisposed)
        {
            _workspaceOrdersTab = CreateOrdersTab();
        }

        if (!_workspaceProcessTabs.TabPages.Contains(_workspaceCustomersTab))
        {
            _workspaceProcessTabs.TabPages.Add(_workspaceCustomersTab);
        }

        if (!_workspaceProcessTabs.TabPages.Contains(_workspaceOrdersTab))
        {
            _workspaceProcessTabs.TabPages.Add(_workspaceOrdersTab);
        }
    }

    private TabPage? GetProcessTab(WorkspaceProcessFocus focus)
    {
        if (_workspaceProcessTabs is null)
        {
            return null;
        }

        if (focus == WorkspaceProcessFocus.Customers)
        {
            if (_workspaceCustomersTab is not null && _workspaceProcessTabs.TabPages.Contains(_workspaceCustomersTab))
            {
                return _workspaceCustomersTab;
            }

            return _workspaceProcessTabs.TabPages.Cast<TabPage>().FirstOrDefault();
        }

        if (_workspaceOrdersTab is not null && _workspaceProcessTabs.TabPages.Contains(_workspaceOrdersTab))
        {
            return _workspaceOrdersTab;
        }

        return _workspaceProcessTabs.TabPages.Cast<TabPage>().Skip(1).FirstOrDefault()
               ?? _workspaceProcessTabs.TabPages.Cast<TabPage>().FirstOrDefault();
    }

    private void ApplyWorkspaceProcessFocus(WorkspaceProcessFocus focus)
    {
        if (focus == WorkspaceProcessFocus.Customers && _customersGrid.Rows.Count > 0)
        {
            _customersGrid.CurrentCell = _customersGrid.Rows[0].Cells[0];
            _customersGrid.Rows[0].Selected = true;
            _customersGrid.Focus();
        }
        else if (_ordersGrid.Rows.Count > 0)
        {
            _ordersGrid.CurrentCell = _ordersGrid.Rows[0].Cells[0];
            _ordersGrid.Rows[0].Selected = true;
            _ordersGrid.Focus();
        }
    }

    private Control CreateIncomingOrdersShell()
    {
        var shell = CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.Height = 268;
        shell.Padding = new Padding(16);
        shell.Margin = new Padding(0, 0, 0, 12);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader(
            "Новые заказы",
            "Приоритетная очередь: подтвердить -> резерв -> счет -> отгрузка -> печать."),
            0,
            0);
        root.Controls.Add(CreateIncomingOrdersToolbar(), 0, 1);
        root.Controls.Add(_incomingOrdersGrid, 0, 2);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateIncomingOrdersToolbar()
    {
        ConfigureDigestLabel(_incomingOrdersFilteredLabel);

        var panel = DesktopSurfaceFactory.CreateToolbarStrip(bottomPadding: 10);
        panel.Controls.Add(_incomingOrdersFilteredLabel);
        panel.Controls.Add(CreateActionButton("Подтвердить", (_, _) => ConfirmSelectedOrder()));
        panel.Controls.Add(CreateActionButton("В резерв", (_, _) => ReserveSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Сформировать счет", (_, _) => CreateInvoiceFromSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Подготовить отгрузку", (_, _) => CreateShipmentFromSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Печать", (_, _) => PrintSelectedOrder()));
        return panel;
    }

    private void HandleWorkspaceSearchChanged(object? sender, EventArgs e)
    {
        var value = _workspaceSearchTextBox.Text;
        if (_customerSearchTextBox.Text != value)
        {
            _customerSearchTextBox.Text = value;
        }

        if (_orderSearchTextBox.Text != value)
        {
            _orderSearchTextBox.Text = value;
        }

        ScheduleCustomersRefresh();
        ScheduleOrdersRefresh();
    }

    private void HandleCustomerSearchChanged(object? sender, EventArgs e)
    {
        ScheduleCustomersRefresh();
    }

    private void HandleOrderSearchChanged(object? sender, EventArgs e)
    {
        ScheduleOrdersRefresh();
    }

    private void ScheduleCustomersRefresh()
    {
        _customerSearchDebounceTimer.Stop();
        _customerSearchDebounceTimer.Start();
    }

    private void ScheduleOrdersRefresh()
    {
        _orderSearchDebounceTimer.Stop();
        _orderSearchDebounceTimer.Start();
    }

    private void HandleCustomerSearchDebounceTick(object? sender, EventArgs e)
    {
        _customerSearchDebounceTimer.Stop();
        RefreshCustomers();
    }

    private void HandleOrderSearchDebounceTick(object? sender, EventArgs e)
    {
        _orderSearchDebounceTimer.Stop();
        RefreshOrders();
    }

    private void ConfigureOrderStatusFilterComboBox()
    {
        _orderStatusFilterComboBox.Width = 176;
        _orderStatusFilterComboBox.Height = 36;
        _orderStatusFilterComboBox.Font = DesktopTheme.BodyFont(10f);
        _orderStatusFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _orderStatusFilterComboBox.FlatStyle = FlatStyle.Flat;
        _orderStatusFilterComboBox.BackColor = Color.White;
        _orderStatusFilterComboBox.ForeColor = Color.FromArgb(52, 64, 91);
        _orderStatusFilterComboBox.SelectedIndexChanged += (_, _) => ScheduleOrdersRefresh();
    }

    private void RefreshOrderStatusFilterOptions()
    {
        var current = TextMojibakeFixer.NormalizeText(_orderStatusFilterComboBox.SelectedItem as string);
        var statuses = _workspace.OrderStatuses
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(TextMojibakeFixer.NormalizeText)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(item => item, StringComparer.OrdinalIgnoreCase)
            .ToList();

        statuses.Insert(0, AllOrderStatusesFilter);

        _orderStatusFilterComboBox.BeginUpdate();
        try
        {
            _orderStatusFilterComboBox.Items.Clear();
            foreach (var status in statuses)
            {
                _orderStatusFilterComboBox.Items.Add(status);
            }

            var selected = !string.IsNullOrWhiteSpace(current) && statuses.Contains(current, StringComparer.OrdinalIgnoreCase)
                ? current
                : AllOrderStatusesFilter;
            _orderStatusFilterComboBox.SelectedItem = selected;
        }
        finally
        {
            _orderStatusFilterComboBox.EndUpdate();
        }
    }

    private Control CreateWorkspaceRegistryColumn(string title, string subtitle, Control toolbar, Control grid)
    {
        var shell = CreateCardShell();
        shell.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader(title, subtitle), 0, 0);
        root.Controls.Add(toolbar, 0, 1);
        root.Controls.Add(grid, 0, 2);
        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateWorkspaceDetailsColumn()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 52));

        root.Controls.Add(CreateCustomerDetailsShell(), 0, 0);
        root.Controls.Add(CreateOrderDetailsShell(), 0, 1);
        return root;
    }

    private void EnsureDeferredTabsLoaded(TabControl tabs)
    {
        if (_oneCImportTabPage is null || tabs.SelectedTab != _oneCImportTabPage)
        {
            return;
        }

        QueueOneCImportLoad();
    }

    private Control CreateOneCImportPlaceholder()
    {
        var card = CreateCardShell();
        card.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateSectionHeader(
            "Данные 1С",
            "Тяжелая служебная вкладка открывается отдельно и не блокирует основной экран продаж."), 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(110, 101, 91),
            Text = "Когда нужно посмотреть исходную выгрузку 1С, нажмите кнопку ниже."
        }, 0, 1);

        var button = CreateActionButton("Загрузить данные 1С", (_, _) => QueueOneCImportLoad());
        button.Margin = new Padding(0, 12, 0, 0);
        root.Controls.Add(button, 0, 2);

        card.Controls.Add(root);
        return card;
    }

    private void QueueOneCImportLoad()
    {
        if (_oneCImportLoadQueued || _oneCImportSnapshot is null || _oneCImportTabPage is null || _oneCImportControl is OneCImportWorkspaceControl)
        {
            return;
        }

        _oneCImportLoadQueued = true;
        BeginInvoke(new Action(LoadOneCImportTabContent));
    }

    private void LoadOneCImportTabContent()
    {
        if (_oneCImportSnapshot is null || _oneCImportTabPage is null || _oneCImportControl is OneCImportWorkspaceControl)
        {
            return;
        }

        _oneCImportTabPage.Controls.Clear();
        _oneCImportTabPage.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(102, 94, 85),
            Text = "Загрузка данных 1С..."
        });
        _oneCImportTabPage.Update();

        try
        {
            _oneCImportControl = new OneCImportWorkspaceControl(_oneCImportSnapshot)
            {
                Dock = DockStyle.Fill
            };
            _oneCImportTabPage.Controls.Clear();
            _oneCImportTabPage.Controls.Add(_oneCImportControl);
        }
        catch (Exception exception)
        {
            _oneCImportLoadQueued = false;
            _oneCImportControl = CreateOneCImportPlaceholder();
            _oneCImportTabPage.Controls.Clear();
            _oneCImportTabPage.Controls.Add(new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                Font = new Font("Segoe UI", 10f),
                ForeColor = Color.FromArgb(146, 64, 52),
                Text = $"Не удалось открыть данные 1С.{Environment.NewLine}{Environment.NewLine}{exception.Message}"
            });
        }
    }

    private TabPage CreateCustomersTab()
    {
        var tab = new TabPage("Покупатели")
        {
            Padding = new Padding(10)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateCustomersToolbar(), 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateGridShell("Реестр покупателей", "Поиск по коду, названию, менеджеру и статусу.", _customersGrid), 0, 0);
        content.Controls.Add(CreateCustomerDetailsShell(), 1, 0);

        root.Controls.Add(content, 0, 1);
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreateOrdersTab()
    {
        var tab = new TabPage("Заказы")
        {
            Padding = new Padding(10)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateOrdersToolbar(), 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.Controls.Add(CreateGridShell("Реестр заказов покупателей", "Номер, дата, клиент, склад, статус и менеджер.", _ordersGrid), 0, 0);
        content.Controls.Add(CreateOrderDetailsShell(), 1, 0);

        root.Controls.Add(content, 0, 1);
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreateOperationsTab()
    {
        var tab = new TabPage("Журнал")
        {
            Padding = new Padding(10)
        };

        _operationsGrid.DataSource = _operationsBindingSource;

        var card = CreateCardShell();
        card.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader("Журнал операций", "Кто и когда подтвердил заказ, выставил счет, поставил резерв или провел отгрузку."), 0, 0);
        root.Controls.Add(_operationsGrid, 0, 1);
        card.Controls.Add(root);
        tab.Controls.Add(card);
        return tab;
    }

    private Control CreateCustomersToolbar()
    {
        _customerSearchTextBox.Width = 260;
        _customerSearchTextBox.Font = new Font("Segoe UI", 10f);
        _customerSearchTextBox.PlaceholderText = "Поиск клиента";
        _customerSearchTextBox.Margin = new Padding(0, 2, 8, 8);
        _customerSearchTextBox.TextChanged -= HandleCustomerSearchChanged;
        _customerSearchTextBox.TextChanged += HandleCustomerSearchChanged;
        ConfigureDigestLabel(_customersFilteredLabel);

        var panel = DesktopSurfaceFactory.CreateToolbarStrip();

        panel.Controls.Add(_customerSearchTextBox);
        panel.Controls.Add(_customersFilteredLabel);
        panel.Controls.Add(CreateActionButton("Новый покупатель", (_, _) => CreateCustomer()));
        panel.Controls.Add(CreateActionButton("Изменить покупателя", (_, _) => EditSelectedCustomer()));
        panel.Controls.Add(CreateActionButton("Новый заказ по клиенту", (_, _) => CreateOrderForSelectedCustomer()));
        return panel;
    }

    private Control CreateOrdersToolbar()
    {
        _orderSearchTextBox.Width = 260;
        _orderSearchTextBox.Font = new Font("Segoe UI", 10f);
        _orderSearchTextBox.PlaceholderText = "Поиск заказа";
        _orderSearchTextBox.Margin = new Padding(0, 2, 8, 8);
        _orderStatusFilterComboBox.Margin = new Padding(0, 2, 8, 8);
        _orderSearchTextBox.TextChanged -= HandleOrderSearchChanged;
        _orderSearchTextBox.TextChanged += HandleOrderSearchChanged;
        RefreshOrderStatusFilterOptions();
        ConfigureDigestLabel(_ordersFilteredLabel);

        var panel = DesktopSurfaceFactory.CreateToolbarStrip();

        panel.Controls.Add(_orderSearchTextBox);
        panel.Controls.Add(_orderStatusFilterComboBox);
        panel.Controls.Add(_ordersFilteredLabel);
        panel.Controls.Add(CreateActionButton("Подтвердить", (_, _) => ConfirmSelectedOrder()));
        panel.Controls.Add(CreateActionButton("В резерв", (_, _) => ReserveSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Снять резерв", (_, _) => ReleaseReserveForSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Изменить заказ", (_, _) => EditSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Сформировать счет", (_, _) => CreateInvoiceFromSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Подготовить отгрузку", (_, _) => CreateShipmentFromSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Печать", (_, _) => PrintSelectedOrder()));
        return panel;
    }

    private static Button CreateActionButton(string text, EventHandler handler)
    {
        var button = DesktopSurfaceFactory.CreateActionButton(
            text,
            handler,
            DesktopButtonTone.Secondary,
            new Padding(0, 0, 8, 8));
        button.Padding = new Padding(10, 6, 10, 6);
        button.Font = DesktopTheme.EmphasisFont(8.9f);
        button.MinimumSize = new Size(0, 31);
        return button;
    }

    private static Button CreateLaunchpadButton(
        string text,
        EventHandler handler,
        DesktopButtonTone tone,
        Padding? margin = null)
    {
        var button = DesktopSurfaceFactory.CreateActionButton(text, handler, tone, margin);
        button.Padding = new Padding(12, 7, 12, 7);
        button.Font = DesktopTheme.EmphasisFont(9f);
        button.MinimumSize = new Size(0, 33);
        return button;
    }

    private static void ConfigureDigestLabel(Label label)
    {
        label.AutoSize = true;
        label.Padding = new Padding(10, 4, 10, 4);
        label.Margin = new Padding(0, 7, 6, 0);
        label.Font = DesktopTheme.EmphasisFont(8.8f);
        label.BackColor = DesktopTheme.SurfaceMuted;
        label.ForeColor = DesktopTheme.TextSecondary;
    }

    private Control CreateCustomerDetailsShell()
    {
        var shell = CreateCardShell();
        shell.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader("Карточка покупателя", "Контактные данные, договор и текущие продажи клиента."), 0, 0);
        root.Controls.Add(CreateCustomerSummaryGrid(), 0, 1);
        root.Controls.Add(CreateGridShell("Заказы клиента", "Текущая очередь заказов выбранного покупателя.", _customerOrdersGrid), 0, 2);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateCustomerSummaryGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 12)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateValueCard("Клиент", _customerNameValueLabel), 0, 0);
        grid.Controls.Add(CreateValueCard("Договор", _customerContractValueLabel), 1, 0);
        grid.Controls.Add(CreateValueCard("Менеджер", _customerManagerValueLabel), 0, 1);
        grid.Controls.Add(CreateValueCard("Телефон", _customerPhoneValueLabel), 1, 1);
        grid.Controls.Add(CreateValueCard("Email", _customerEmailValueLabel), 0, 2);
        grid.Controls.Add(CreateValueCard("Комментарий", _customerNotesValueLabel), 1, 2);
        return grid;
    }

    private Control CreateOrderDetailsShell()
    {
        var shell = CreateCardShell();
        shell.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader("Состав заказа", "Шапка документа и табличная часть по позициям."), 0, 0);
        root.Controls.Add(CreateOrderSummaryGrid(), 0, 1);
        root.Controls.Add(CreateGridShell("Позиции", "Строки выбранного заказа покупателя.", _orderLinesGrid), 0, 2);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateOrderSummaryGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 12)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        grid.Controls.Add(CreateValueCard("Номер", _orderNumberValueLabel), 0, 0);
        grid.Controls.Add(CreateValueCard("Клиент", _orderCustomerValueLabel), 1, 0);
        grid.Controls.Add(CreateValueCard("Склад", _orderWarehouseValueLabel), 0, 1);
        grid.Controls.Add(CreateValueCard("Статус", _orderStatusValueLabel), 1, 1);
        grid.Controls.Add(CreateValueCard("Менеджер", _orderManagerValueLabel), 0, 2);
        grid.Controls.Add(CreateValueCard("Сумма", _orderTotalValueLabel), 1, 2);
        grid.Controls.Add(CreateValueCard("Обеспечение", _orderSupplyValueLabel), 0, 3);
        grid.Controls.Add(CreateValueCard("Складские действия", _orderWarehouseActionsValueLabel), 1, 3);
        grid.Controls.Add(CreateValueCard("Комментарий", _orderCommentValueLabel), 0, 4);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 4)!, 2);
        return grid;
    }

    private TabPage CreateReadonlyTab(string title, string subtitle, object dataSource)
    {
        if (ReferenceEquals(dataSource, _demoWorkspace.SalesInvoices))
        {
            return CreateHostedTabPage("Счета", _invoicesTabControl);
        }

        if (ReferenceEquals(dataSource, _demoWorkspace.SalesShipments))
        {
            return CreateHostedTabPage("Отгрузки", _shipmentsTabControl);
        }

        var tab = new TabPage(title)
        {
            Padding = new Padding(10)
        };

        var card = CreateCardShell();
        card.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader(title, subtitle), 0, 0);
        root.Controls.Add(DesktopGridFactory.CreateGrid(dataSource), 0, 1);
        card.Controls.Add(root);
        tab.Controls.Add(card);
        return tab;
    }

    private TabPage CreateHostedTabPage(string title, Control content)
    {
        var tab = new TabPage(title)
        {
            Padding = new Padding(10)
        };

        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        if (ReferenceEquals(content, _oneCImportControl))
        {
            _oneCImportTabPage = tab;
        }

        return tab;
    }

    private void WireBindings()
    {
        _customersTabControl.NavigateRequested += HandleWorkspaceNavigationRequested;
        _invoicesTabControl.NavigateRequested += HandleWorkspaceNavigationRequested;
        _shipmentsTabControl.NavigateRequested += HandleWorkspaceNavigationRequested;
        _invoicesTabControl.DocumentsChanged += (_, _) => HandleRelatedDocumentsChanged();
        _shipmentsTabControl.DocumentsChanged += (_, _) => HandleRelatedDocumentsChanged();

        _customersGrid.DataSource = _customersBindingSource;
        _customersGrid.SelectionChanged += (_, _) => RefreshCustomerDetails();
        _customersGrid.DoubleClick += (_, _) => EditSelectedCustomer();

        _customerOrdersGrid.DataSource = _customerOrdersBindingSource;

        _ordersGrid.DataSource = _ordersBindingSource;
        _ordersGrid.SelectionChanged += (_, _) => HandleOrdersGridSelectionChanged();
        _ordersGrid.DoubleClick += (_, _) => EditSelectedOrder();

        _incomingOrdersGrid.DataSource = _incomingOrdersBindingSource;
        _incomingOrdersGrid.SelectionChanged += (_, _) => HandleIncomingOrdersGridSelectionChanged();
        _incomingOrdersGrid.DoubleClick += (_, _) => EditSelectedOrder();

        _orderLinesGrid.DataSource = _orderLinesBindingSource;
        _recentShipmentsGrid.DataSource = _recentShipmentsBindingSource;
    }

    private void HandleWorkspaceNavigationRequested(SalesWorkspaceNavigationTarget target)
    {
        switch (target)
        {
            case SalesWorkspaceNavigationTarget.Orders:
                if (_mainTabsControl is not null && _mainTabsControl.TabPages.Count > 0)
                {
                    _mainTabsControl.SelectedIndex = 0;
                    ShowWorkspaceDashboard();
                }

                break;
            case SalesWorkspaceNavigationTarget.Customers:
                NavigateToMainTab("Клиенты");
                break;
            case SalesWorkspaceNavigationTarget.Invoices:
                NavigateToMainTab("Счета");
                break;
            case SalesWorkspaceNavigationTarget.Shipments:
                NavigateToMainTab("Отгрузки");
                break;
        }
    }

    private void HandleOrdersGridSelectionChanged()
    {
        RefreshOrderDetails();
        SyncIncomingOrderSelection(GetSelectedOrderId());
    }

    private void HandleIncomingOrdersGridSelectionChanged()
    {
        if (_syncingOrderSelection)
        {
            return;
        }

        if (_incomingOrdersGrid.CurrentRow?.DataBoundItem is not IncomingOrderRow row)
        {
            return;
        }

        SelectOrderInMainGrid(row.OrderId);
        RefreshOrderDetails();
    }

    private void SelectOrderInMainGrid(Guid orderId)
    {
        _syncingOrderSelection = true;
        try
        {
            foreach (DataGridViewRow row in _ordersGrid.Rows)
            {
                if (row.DataBoundItem is OrderGridRow data && data.OrderId == orderId)
                {
                    row.Selected = true;
                    _ordersGrid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }
        finally
        {
            _syncingOrderSelection = false;
        }
    }

    private void SyncIncomingOrderSelection(Guid? orderId)
    {
        _syncingOrderSelection = true;
        try
        {
            SelectGridRowById<IncomingOrderRow>(_incomingOrdersGrid, row => row.OrderId, orderId);
        }
        finally
        {
            _syncingOrderSelection = false;
        }
    }

    private static void SelectGridRowById<TRow>(
        DataGridView grid,
        Func<TRow, Guid> keySelector,
        Guid? selectedId)
    {
        grid.ClearSelection();
        if (selectedId is null)
        {
            return;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is TRow data && keySelector(data) == selectedId.Value)
            {
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                return;
            }
        }
    }

    private void RefreshAll()
    {
        SuspendLayout();
        try
        {
            _customerSearchDebounceTimer.Stop();
            _orderSearchDebounceTimer.Stop();
            RefreshCustomers();
            RefreshOrders();
            _customersTabControl.RefreshView();
            _invoicesTabControl.RefreshView();
            _shipmentsTabControl.RefreshView();
            RefreshOperationsLog();
            RefreshSummaryCards();
            TextMojibakeFixer.NormalizeControlTree(this);
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void RefreshCustomers(Guid? selectedCustomerId = null)
    {
        var currentId = selectedCustomerId ?? GetSelectedCustomerId();
        var search = _customerSearchTextBox.Text.Trim();

        var rows = _workspace.Customers
            .Where(customer => string.IsNullOrWhiteSpace(search) || MatchesCustomerSearch(customer, search))
            .Select(customer => new CustomerGridRow(
                customer.Id,
                customer.Code,
                customer.Name,
                customer.ContractNumber,
                customer.Manager,
                customer.Status))
            .OrderBy(row => row.Name)
            .ToArray();

        _customersBindingSource.DataSource = rows;
        _customersFilteredLabel.Text = $"Показано: {rows.Length:N0} из {_workspace.Customers.Count:N0}";
        RestoreGridSelection<CustomerGridRow>(_customersGrid, row => row.CustomerId, currentId);
        RefreshCustomerDetails();
        RefreshSummaryCards();
    }

    private void RefreshOrders(Guid? selectedOrderId = null)
    {
        var currentId = selectedOrderId ?? GetSelectedOrderId();
        var search = _orderSearchTextBox.Text.Trim();
        var selectedStatus = _orderStatusFilterComboBox.SelectedItem as string ?? AllOrderStatusesFilter;
        var dateFrom = _ordersDateFromPicker.Value.Date;
        var dateTo = _ordersDateToPicker.Value.Date;
        if (dateFrom > dateTo)
        {
            (dateFrom, dateTo) = (dateTo, dateFrom);
        }

        var rows = _workspace.Orders
            .Where(order => string.IsNullOrWhiteSpace(search) || MatchesOrderSearch(order, search))
            .Where(order => string.Equals(selectedStatus, AllOrderStatusesFilter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.Status, selectedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(order => order.OrderDate.Date >= dateFrom && order.OrderDate.Date <= dateTo)
            .OrderByDescending(order => order.OrderDate)
            .ThenByDescending(order => order.Number)
            .Select(order => new OrderGridRow(
                order.Id,
                order.Number,
                order.OrderDate.ToString("dd.MM.yyyy"),
                order.CustomerName,
                order.Warehouse,
                order.PositionCount,
                $"{order.TotalAmount:N2} ₽",
                order.Status,
                order.Manager))
            .ToArray();

        _ordersBindingSource.DataSource = rows;
        _ordersFilteredLabel.Text = string.Equals(selectedStatus, AllOrderStatusesFilter, StringComparison.OrdinalIgnoreCase)
            ? $"Показано: {rows.Length:N0} из {_workspace.Orders.Count:N0}"
            : $"Показано: {rows.Length:N0} из {_workspace.Orders.Count:N0} ({selectedStatus})";
        RestoreGridSelection<OrderGridRow>(_ordersGrid, row => row.OrderId, currentId);
        RefreshIncomingOrders(currentId);
        RefreshOrderDetails();
        RefreshSummaryCards();
    }

    private void RefreshIncomingOrders(Guid? selectedOrderId = null)
    {
        var search = string.IsNullOrWhiteSpace(_workspaceSearchTextBox.Text)
            ? _orderSearchTextBox.Text.Trim()
            : _workspaceSearchTextBox.Text.Trim();
        var selectedStatus = _orderStatusFilterComboBox.SelectedItem as string ?? AllOrderStatusesFilter;
        var dateFrom = _ordersDateFromPicker.Value.Date;
        var dateTo = _ordersDateToPicker.Value.Date;
        if (dateFrom > dateTo)
        {
            (dateFrom, dateTo) = (dateTo, dateFrom);
        }

        var allIncomingCount = _workspace.Orders.Count();

        var rows = _workspace.Orders
            .Where(order => string.IsNullOrWhiteSpace(search) || MatchesOrderSearch(order, search))
            .Where(order => string.Equals(selectedStatus, AllOrderStatusesFilter, StringComparison.OrdinalIgnoreCase)
                || string.Equals(order.Status, selectedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(order => order.OrderDate.Date >= dateFrom && order.OrderDate.Date <= dateTo)
            .OrderBy(order => order.OrderDate)
            .ThenBy(order => order.Number)
            .Select(order => new IncomingOrderRow(
                order.Id,
                order.Number,
                order.CustomerName,
                order.OrderDate.ToString("dd.MM.yyyy"),
                $"{order.TotalAmount:N2} ₽",
                order.Status,
                order.OrderDate.AddDays(3).ToString("dd.MM.yyyy"),
                "..."))
            .ToArray();

        _incomingOrdersBindingSource.DataSource = rows;
        var shownFrom = rows.Length == 0 ? 0 : 1;
        _incomingOrdersFilteredLabel.Text = $"Показано {shownFrom:N0}–{rows.Length:N0} из {allIncomingCount:N0}";
        ConfigureDashboardOrdersGridColumns();
        SyncIncomingOrderSelection(selectedOrderId ?? GetSelectedOrderId());
    }

    private void ConfigureDashboardOrdersGridColumns()
    {
        if (_incomingOrdersGrid.Columns.Count == 0)
        {
            return;
        }

        _incomingOrdersGrid.RowTemplate.Height = 38;
        _incomingOrdersGrid.ColumnHeadersHeight = 36;
        _incomingOrdersGrid.ColumnHeadersDefaultCellStyle.Font = DesktopTheme.EmphasisFont(9.4f);
        _incomingOrdersGrid.DefaultCellStyle.Font = DesktopTheme.BodyFont(10f);
        _incomingOrdersGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(238, 243, 255);
        _incomingOrdersGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(37, 50, 84);
        _incomingOrdersGrid.GridColor = Color.FromArgb(233, 238, 248);
        _incomingOrdersGrid.BackgroundColor = Color.White;
        _incomingOrdersGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;

        foreach (DataGridViewColumn column in _incomingOrdersGrid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = 80;

            switch (column.DataPropertyName)
            {
                case "Number":
                    column.Width = 130;
                    break;
                case "Customer":
                    column.Width = 238;
                    break;
                case "Date":
                    column.Width = 122;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case "Total":
                    column.Width = 138;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    break;
                case "Status":
                    column.Width = 162;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case "DeliveryDate":
                    column.Width = 130;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case "Actions":
                    column.Width = 90;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
            }
        }
    }

    private void ConfigureRecentShipmentsGridColumns()
    {
        if (_recentShipmentsGrid.Columns.Count == 0)
        {
            return;
        }

        _recentShipmentsGrid.RowTemplate.Height = 34;
        _recentShipmentsGrid.ColumnHeadersHeight = 34;
        _recentShipmentsGrid.ColumnHeadersDefaultCellStyle.Font = DesktopTheme.EmphasisFont(9.2f);
        _recentShipmentsGrid.DefaultCellStyle.Font = DesktopTheme.BodyFont(9.6f);
        _recentShipmentsGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(238, 243, 255);
        _recentShipmentsGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(37, 50, 84);
        _recentShipmentsGrid.GridColor = Color.FromArgb(233, 238, 248);
        _recentShipmentsGrid.BackgroundColor = Color.White;
        _recentShipmentsGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;

        foreach (DataGridViewColumn column in _recentShipmentsGrid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = 72;

            switch (column.DataPropertyName)
            {
                case "Number":
                    column.Width = 112;
                    break;
                case "OrderNumber":
                    column.Width = 108;
                    break;
                case "Customer":
                    column.Width = 170;
                    break;
                case "Date":
                    column.Width = 94;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case "Status":
                    column.Width = 114;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
            }
        }
    }

    private static bool IsIncomingOrderStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        return status.Equals("План", StringComparison.OrdinalIgnoreCase)
            || status.Contains("нов", StringComparison.OrdinalIgnoreCase)
            || status.Contains("чернов", StringComparison.OrdinalIgnoreCase)
            || status.Contains("проверк", StringComparison.OrdinalIgnoreCase)
            || status.Contains("new", StringComparison.OrdinalIgnoreCase)
            || status.Contains("draft", StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshCustomerDetails()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            SetText(_customerNameValueLabel, "-");
            SetText(_customerContractValueLabel, "-");
            SetText(_customerManagerValueLabel, "-");
            SetText(_customerPhoneValueLabel, "-");
            SetText(_customerEmailValueLabel, "-");
            SetText(_customerNotesValueLabel, "-");
            _customerOrdersBindingSource.DataSource = Array.Empty<CustomerOrderRow>();
            return;
        }

        SetText(_customerNameValueLabel, $"{customer.Name} [{customer.Code}]");
        SetText(_customerContractValueLabel, customer.ContractNumber);
        SetText(_customerManagerValueLabel, customer.Manager);
        SetText(_customerPhoneValueLabel, customer.Phone);
        SetText(_customerEmailValueLabel, customer.Email);
        SetText(_customerNotesValueLabel, customer.Notes);

        _customerOrdersBindingSource.DataSource = _workspace.Orders
            .Where(order => order.CustomerId == customer.Id)
            .OrderByDescending(order => order.OrderDate)
            .Select(order => new CustomerOrderRow(
                order.Number,
                order.OrderDate.ToString("dd.MM.yyyy"),
                order.Status,
                $"{order.TotalAmount:N2} ₽"))
            .ToArray();
    }

    private void RefreshOrderDetails()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            SetText(_orderNumberValueLabel, "-");
            SetText(_orderCustomerValueLabel, "-");
            SetText(_orderWarehouseValueLabel, "-");
            SetText(_orderStatusValueLabel, "-");
            SetText(_orderManagerValueLabel, "-");
            SetText(_orderTotalValueLabel, "-");
            SetText(_orderSupplyValueLabel, "-");
            SetText(_orderWarehouseActionsValueLabel, "-");
            SetText(_orderCommentValueLabel, "-");
            _orderLinesBindingSource.DataSource = Array.Empty<OrderLineGridRow>();
            return;
        }

        var inventory = new SalesInventoryService(_workspace);
        var supplyCheck = inventory.AnalyzeOrder(order);
        var warehouseActionsLabel = BuildWarehouseActionsLabel(order);

        SetText(_orderNumberValueLabel, $"{order.Number} от {order.OrderDate:dd.MM.yyyy}");
        SetText(_orderCustomerValueLabel, $"{order.CustomerName} [{order.CustomerCode}]");
        SetText(_orderWarehouseValueLabel, order.Warehouse);
        SetText(_orderStatusValueLabel, order.Status);
        SetText(_orderManagerValueLabel, order.Manager);
        SetText(_orderTotalValueLabel, $"{order.TotalAmount:N2} ₽");
        SetText(_orderSupplyValueLabel, BuildSupplyLabel(supplyCheck));
        SetText(_orderWarehouseActionsValueLabel, warehouseActionsLabel);
        SetText(_orderCommentValueLabel, string.IsNullOrWhiteSpace(order.Comment) ? "-" : order.Comment);

        _orderLinesBindingSource.DataSource = order.Lines
            .Select(line => new OrderLineGridRow(
                line.ItemCode,
                line.ItemName,
                line.Unit,
                line.Quantity,
                $"{line.Price:N2} ₽",
                $"{line.Amount:N2} ₽"))
            .ToArray();
    }

    private void RefreshSummaryCards()
    {
        var activeCustomers = _workspace.Customers.Count(customer =>
        {
            var status = TextMojibakeFixer.NormalizeText(customer.Status);
            return status.Contains("актив", StringComparison.OrdinalIgnoreCase);
        });
        if (activeCustomers == 0)
        {
            activeCustomers = _workspace.Customers.Count;
        }

        var activeOrders = _workspace.Orders.Count(order =>
        {
            var status = TextMojibakeFixer.NormalizeText(order.Status);
            return !status.Contains("отгруж", StringComparison.OrdinalIgnoreCase)
                   && !status.Contains("закрыт", StringComparison.OrdinalIgnoreCase)
                   && !status.Contains("архив", StringComparison.OrdinalIgnoreCase);
        });
        var invoiceCount = _workspace.Invoices.Count;
        var shipmentCount = _workspace.Shipments.Count;

        _activeCustomersValueLabel.Text = activeCustomers.ToString();
        _ordersInWorkValueLabel.Text = activeOrders.ToString();
        _pipelineValueLabel.Text = invoiceCount.ToString();
        _reserveValueLabel.Text = shipmentCount.ToString();

        RefreshRecentShipments();
        RefreshOrdersStatusWidget();
    }

    private void RefreshRecentShipments()
    {
        _recentShipmentsBindingSource.DataSource = _workspace.Shipments
            .OrderByDescending(item => item.ShipmentDate)
            .ThenByDescending(item => item.Number)
            .Take(7)
            .Select(item => new RecentShipmentRow(
                item.Number,
                item.SalesOrderNumber,
                item.CustomerName,
                item.ShipmentDate.ToString("dd.MM.yyyy"),
                item.Status))
            .ToArray();
        ConfigureRecentShipmentsGridColumns();
    }

    private void RefreshOrdersStatusWidget()
    {
        var slices = _workspace.Orders
            .GroupBy(item => item.Status)
            .OrderByDescending(group => group.Count())
            .Take(6)
            .Select((group, index) => new OrderStatusBreakdownSlice(
                NormalizeStatusLabel(group.Key),
                group.Count(),
                ResolveStatusColor(index)))
            .ToArray();

        _ordersStatusSlices = slices;
        _ordersStatusLegendPanel.SuspendLayout();
        _ordersStatusLegendPanel.Controls.Clear();
        var total = Math.Max(1, slices.Sum(item => item.Count));
        foreach (var slice in slices)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 8),
                Padding = new Padding(0)
            };
            row.Controls.Add(new Panel
            {
                Width = 10,
                Height = 10,
                BackColor = slice.Color,
                Margin = new Padding(0, 6, 8, 0)
            });
            row.Controls.Add(new Label
            {
                AutoSize = true,
                Font = DesktopTheme.BodyFont(9.4f),
                ForeColor = DesktopTheme.TextPrimary,
                Text = $"{slice.Status}  {slice.Count} ({(slice.Count * 100m / total):N0}%)",
                Margin = new Padding(0, 2, 0, 0)
            });
            _ordersStatusLegendPanel.Controls.Add(row);
        }

        _ordersStatusLegendPanel.ResumeLayout(true);
        _ordersStatusTotalLabel.Text = $"{_workspace.Orders.Count}{Environment.NewLine}Всего";
        _ordersStatusDonutPanel.Invalidate();
    }

    private void HandleOrdersStatusDonutPaint(object? sender, PaintEventArgs e)
    {
        if (sender is not Panel panel)
        {
            return;
        }

        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(16, 16, Math.Max(0, panel.Width - 32), Math.Max(0, panel.Height - 32));
        if (bounds.Width <= 24 || bounds.Height <= 24)
        {
            return;
        }

        var total = _ordersStatusSlices.Sum(item => item.Count);
        if (total <= 0)
        {
            using var emptyPen = new Pen(DesktopTheme.BorderStrong, 18);
            e.Graphics.DrawArc(emptyPen, bounds, -90, 360);
            return;
        }

        float start = -90f;
        foreach (var slice in _ordersStatusSlices)
        {
            var sweep = (float)(slice.Count * 360d / total);
            if (sweep <= 0.01f)
            {
                continue;
            }

            using var pen = new Pen(slice.Color, 18f);
            e.Graphics.DrawArc(pen, bounds, start, sweep);
            start += sweep;
        }
    }

    private static string NormalizeStatusLabel(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return string.IsNullOrWhiteSpace(normalized) ? "Без статуса" : normalized;
    }

    private static Color ResolveStatusColor(int index)
    {
        return index switch
        {
            0 => Color.FromArgb(115, 137, 245),
            1 => Color.FromArgb(104, 192, 122),
            2 => Color.FromArgb(246, 185, 88),
            3 => Color.FromArgb(150, 122, 224),
            4 => Color.FromArgb(83, 172, 214),
            _ => Color.FromArgb(233, 116, 116)
        };
    }

    private void RefreshOperationsLog()
    {
        _operationsBindingSource.DataSource = _workspace.OperationLog
            .Select(entry => new OperationLogRow(
                entry.Id,
                entry.LoggedAt.ToString("dd.MM.yyyy HH:mm"),
                entry.Actor,
                entry.EntityType,
                entry.EntityNumber,
                entry.Action,
                entry.Result,
                entry.Message))
            .ToArray();
    }

    private void CreateCustomer()
    {
        using var form = new CustomerEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultCustomer is null)
        {
            return;
        }

        _workspace.AddCustomer(form.ResultCustomer);
        RefreshCustomers(form.ResultCustomer.Id);
    }

    private void EditSelectedCustomer()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            return;
        }

        using var form = new CustomerEditorForm(_workspace, customer);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultCustomer is null)
        {
            return;
        }

        _workspace.UpdateCustomer(form.ResultCustomer);
        RefreshCustomers(form.ResultCustomer.Id);
        RefreshOrders();
        _invoicesTabControl.RefreshView();
        _shipmentsTabControl.RefreshView();
    }

    private void CreateOrder()
    {
        using var form = new SalesOrderEditorForm(_workspace, null, GetSelectedCustomerId());
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultOrder is null)
        {
            return;
        }

        _workspace.AddOrder(form.ResultOrder);
        RefreshOrders(form.ResultOrder.Id);
        RefreshCustomers(form.ResultOrder.CustomerId);
    }

    private void CreateOrderForSelectedCustomer()
    {
        CreateOrder();
    }

    private void EditSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            return;
        }

        using var form = new SalesOrderEditorForm(_workspace, order);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultOrder is null)
        {
            return;
        }

        _workspace.UpdateOrder(form.ResultOrder);
        RefreshOrders(form.ResultOrder.Id);
        RefreshCustomers(form.ResultOrder.CustomerId);
        _invoicesTabControl.RefreshView();
        _shipmentsTabControl.RefreshView();
    }

    private void DuplicateSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            return;
        }

        var copy = _workspace.DuplicateOrder(order.Id);
        RefreshOrders(copy.Id);
        RefreshCustomers(copy.CustomerId);
    }

    private void PrintSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, который нужно открыть в печатной форме.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать заказа {order.Number}",
            SalesDocumentPrintComposer.BuildOrderHtml(order));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void ConfirmSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, который нужно подтвердить.");
            return;
        }

        HandleOrderWorkflowResult(_workspace.ConfirmOrder(order.Id), order.Id, order.CustomerId);
    }

    private void ReserveSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, который нужно поставить в резерв.");
            return;
        }

        HandleOrderWorkflowResult(_workspace.ReserveOrder(order.Id), order.Id, order.CustomerId);
    }

    private void ReleaseReserveForSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, для которого нужно снять резерв.");
            return;
        }

        HandleOrderWorkflowResult(_workspace.ReleaseOrderReserve(order.Id), order.Id, order.CustomerId);
    }

    private void CreateTransferForSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, для которого нужно создать перемещение.");
            return;
        }

        var inventory = new SalesInventoryService(_workspace);
        var check = inventory.AnalyzeOrder(order);
        if (check.IsFullyCovered)
        {
            MessageBox.Show(
                FindForm(),
                $"Заказ {order.Number} уже обеспечен на складе {order.Warehouse}. Отдельное перемещение не требуется.",
                "Продажи",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        var store = WarehouseOperationalWorkspaceStore.CreateDefault();
        var currentOperator = string.IsNullOrWhiteSpace(_workspace.CurrentOperator) ? Environment.UserName : _workspace.CurrentOperator;
        var warehouseWorkspace = store.LoadOrCreate(currentOperator, _workspace);
        warehouseWorkspace.RefreshReferenceData(_workspace);

        var createdTransfers = BuildTransfersForOrder(order, check, warehouseWorkspace);
        if (createdTransfers.Count == 0)
        {
            MessageBox.Show(
                FindForm(),
                "Не удалось подобрать склад-источник для дефицитных позиций. Проверьте закупку или вручную создайте перемещение в модуле склада.",
                "Продажи",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        foreach (var transfer in createdTransfers)
        {
            warehouseWorkspace.AddTransferOrder(transfer);
        }

        store.Save(warehouseWorkspace);
        _workspace.NotifyExternalChange();
        RefreshOrderDetails();

        var unresolvedCount = CountUnresolvedShortages(check, createdTransfers);
        var detail = unresolvedCount == 0
            ? $"Создано складских документов: {createdTransfers.Count}. Они уже доступны в модуле склада."
            : $"Создано складских документов: {createdTransfers.Count}. Остались непокрытые позиции: {unresolvedCount}.";

        MessageBox.Show(
            FindForm(),
            detail,
            $"Перемещение под заказ {order.Number}",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void HandleOrderWorkflowResult(SalesWorkflowActionResult result, Guid orderId, Guid customerId)
    {
        RefreshOrders(orderId);
        RefreshCustomers(customerId);
        _invoicesTabControl.RefreshView();
        _shipmentsTabControl.RefreshView();
        RefreshOperationsLog();
        RefreshSummaryCards();

        MessageBox.Show(
            FindForm(),
            string.IsNullOrWhiteSpace(result.Detail) ? result.Message : $"{result.Message}{Environment.NewLine}{Environment.NewLine}{result.Detail}",
            "Продажи",
            MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void CreateInvoiceFromSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, из которого нужно сформировать счет.");
            return;
        }

        using var form = new SalesInvoiceEditorForm(_workspace, _workspace.CreateInvoiceDraftFromOrder(order.Id));
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultInvoice is null)
        {
            return;
        }

        _workspace.AddInvoice(form.ResultInvoice);
        RefreshOrders(order.Id);
        RefreshCustomers(order.CustomerId);
        _invoicesTabControl.RefreshView(form.ResultInvoice.Id);
        RefreshSummaryCards();
    }

    private void CreateShipmentFromSelectedOrder()
    {
        var order = GetSelectedOrder();
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, из которого нужно подготовить отгрузку.");
            return;
        }

        using var form = new SalesShipmentEditorForm(_workspace, _workspace.CreateShipmentDraftFromOrder(order.Id));
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultShipment is null)
        {
            return;
        }

        _workspace.AddShipment(form.ResultShipment);
        RefreshOrders(order.Id);
        RefreshCustomers(order.CustomerId);
        _shipmentsTabControl.RefreshView(form.ResultShipment.Id);
        RefreshSummaryCards();
    }

    private void HandleRelatedDocumentsChanged()
    {
        RefreshOrders();
        RefreshCustomers();
        _customersTabControl.RefreshView();
        _invoicesTabControl.RefreshView();
        _shipmentsTabControl.RefreshView();
        RefreshOperationsLog();
        RefreshSummaryCards();
    }

    private List<OperationalWarehouseDocumentRecord> BuildTransfersForOrder(
        SalesOrderRecord order,
        SalesInventoryCheck check,
        OperationalWarehouseWorkspace warehouseWorkspace)
    {
        var groupedLines = new Dictionary<string, List<OperationalWarehouseLineRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in check.Lines.Where(item => item.ShortageQuantity > 0))
        {
            var remaining = line.ShortageQuantity;
            foreach (var alternative in line.Alternatives
                         .Where(item => item.AvailableQuantity > 0
                             && !item.Warehouse.Equals(order.Warehouse, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(item => item.AvailableQuantity))
            {
                if (remaining <= 0)
                {
                    break;
                }

                var quantity = Math.Min(remaining, alternative.AvailableQuantity);
                if (quantity <= 0)
                {
                    continue;
                }

                if (!groupedLines.TryGetValue(alternative.Warehouse, out var lines))
                {
                    lines = [];
                    groupedLines[alternative.Warehouse] = lines;
                }

                lines.Add(new OperationalWarehouseLineRecord
                {
                    Id = Guid.NewGuid(),
                    ItemCode = line.ItemCode,
                    ItemName = line.ItemName,
                    Quantity = quantity,
                    Unit = line.Unit,
                    RelatedDocument = order.Number
                });

                remaining -= quantity;
            }
        }

        var transfers = new List<OperationalWarehouseDocumentRecord>();
        foreach (var entry in groupedLines)
        {
            if (entry.Value.Count == 0)
            {
                continue;
            }

            var draft = warehouseWorkspace.CreateTransferDraft(entry.Key);
            draft.SourceWarehouse = entry.Key;
            draft.TargetWarehouse = order.Warehouse;
            draft.Status = warehouseWorkspace.TransferStatuses.Count > 1
                ? warehouseWorkspace.TransferStatuses[1]
                : draft.Status;
            draft.RelatedDocument = order.Number;
            draft.Comment = $"Перемещение под заказ {order.Number} / {order.CustomerName}";
            draft.Lines = new BindingList<OperationalWarehouseLineRecord>(entry.Value.Select(item => item.Clone()).ToList());
            transfers.Add(draft);
        }

        return transfers;
    }

    private static int CountUnresolvedShortages(
        SalesInventoryCheck check,
        IEnumerable<OperationalWarehouseDocumentRecord> createdTransfers)
    {
        var planned = createdTransfers
            .SelectMany(item => item.Lines)
            .GroupBy(item => item.ItemCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Quantity), StringComparer.OrdinalIgnoreCase);

        return check.Lines.Count(item =>
        {
            if (item.ShortageQuantity <= 0)
            {
                return false;
            }

            var plannedQuantity = planned.TryGetValue(item.ItemCode, out var value) ? value : 0m;
            return plannedQuantity < item.ShortageQuantity;
        });
    }

    private string BuildWarehouseActionsLabel(SalesOrderRecord order)
    {
        var store = WarehouseOperationalWorkspaceStore.CreateDefault();
        var currentOperator = string.IsNullOrWhiteSpace(_workspace.CurrentOperator) ? Environment.UserName : _workspace.CurrentOperator;
        var warehouseWorkspace = store.TryLoadExisting(currentOperator, _workspace.CatalogItems, _workspace.Warehouses);
        if (warehouseWorkspace is null)
        {
            return "Складских задач нет";
        }

        var relatedTransfers = warehouseWorkspace.TransferOrders
            .Where(item => item.RelatedDocument.Equals(order.Number, StringComparison.OrdinalIgnoreCase)
                || item.Comment.Contains(order.Number, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (relatedTransfers.Length == 0)
        {
            return "Складских задач нет";
        }

        var completedCount = relatedTransfers.Count(item => item.Status.Equals("Перемещен", StringComparison.OrdinalIgnoreCase));
        return $"Перемещений: {relatedTransfers.Length}, завершено: {completedCount}";
    }

    private static string BuildSupplyLabel(SalesInventoryCheck check)
    {
        if (check.IsFullyCovered)
        {
            return "Полностью обеспечен";
        }

        var shortageCount = check.Lines.Count(item => item.ShortageQuantity > 0);
        return $"Дефицит по {shortageCount} поз., нужен перенос или закупка";
    }

    private void ShowSelectionWarning(string message)
    {
        MessageBox.Show(FindForm(), message, "Продажи", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private SalesCustomerRecord? GetSelectedCustomer()
    {
        return _customersGrid.CurrentRow?.DataBoundItem is CustomerGridRow row
            ? _workspace.Customers.FirstOrDefault(customer => customer.Id == row.CustomerId)
            : null;
    }

    private Guid? GetSelectedCustomerId()
    {
        return _customersGrid.CurrentRow?.DataBoundItem is CustomerGridRow row ? row.CustomerId : null;
    }

    private SalesOrderRecord? GetSelectedOrder()
    {
        if (_ordersGrid.CurrentRow?.DataBoundItem is OrderGridRow row)
        {
            return _workspace.Orders.FirstOrDefault(order => order.Id == row.OrderId);
        }

        return _incomingOrdersGrid.CurrentRow?.DataBoundItem is IncomingOrderRow incoming
            ? _workspace.Orders.FirstOrDefault(order => order.Id == incoming.OrderId)
            : null;
    }

    private Guid? GetSelectedOrderId()
    {
        if (_ordersGrid.CurrentRow?.DataBoundItem is OrderGridRow row)
        {
            return row.OrderId;
        }

        return _incomingOrdersGrid.CurrentRow?.DataBoundItem is IncomingOrderRow incoming
            ? incoming.OrderId
            : null;
    }

    private static bool MatchesCustomerSearch(SalesCustomerRecord customer, string search)
    {
        return customer.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Manager.Contains(search, StringComparison.OrdinalIgnoreCase)
            || customer.Status.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesOrderSearch(SalesOrderRecord order, string search)
    {
        return order.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
            || order.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || order.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
            || order.Warehouse.Contains(search, StringComparison.OrdinalIgnoreCase)
            || order.Manager.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void HandleStatusCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (e.ColumnIndex >= grid.Columns.Count)
        {
            return;
        }

        var column = grid.Columns[e.ColumnIndex];
        if (!string.Equals(column.DataPropertyName, "Status", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (e.Value is not string status || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        ApplyStatusCellStyle(grid, e, status);
    }

    private static void ApplyStatusCellStyle(DataGridView grid, DataGridViewCellFormattingEventArgs e, string status)
    {
        var style = e.CellStyle ?? new DataGridViewCellStyle(grid.DefaultCellStyle);
        e.CellStyle = style;
        var normalized = TextMojibakeFixer.NormalizeText(status);

        if (normalized.Contains("ошиб", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("error", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("отмен", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(251, 231, 227);
            style.ForeColor = DesktopTheme.Danger;
            return;
        }

        if (normalized.Contains("архив", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("закры", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("выполн", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("paid", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("отгруж", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = DesktopTheme.SurfaceMuted;
            style.ForeColor = DesktopTheme.TextMuted;
            return;
        }

        if (normalized.Contains("чернов", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("нов", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("резерв", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("в работе", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("draft", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("new", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("план", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(235, 240, 255);
            style.ForeColor = Color.FromArgb(69, 90, 186);
            return;
        }

        style.BackColor = Color.FromArgb(229, 244, 234);
        style.ForeColor = Color.FromArgb(49, 146, 87);
    }

    private static Control CreateGridShell(string title, string subtitle, Control grid)
    {
        var panel = CreateCardShell();
        panel.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateSectionHeader(title, subtitle), 0, 0);
        root.Controls.Add(grid, 0, 1);
        panel.Controls.Add(root);
        return panel;
    }

    private static Control CreateSectionHeader(string title, string subtitle)
    {
        var hasSubtitle = !string.IsNullOrWhiteSpace(subtitle);
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = hasSubtitle ? 52 : 34
        };

        if (hasSubtitle)
        {
            header.Controls.Add(new Label
            {
                Text = subtitle,
                Dock = DockStyle.Top,
                Height = 22,
                Font = DesktopTheme.BodyFont(9f),
                ForeColor = DesktopTheme.TextSecondary
            });
        }

        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = hasSubtitle ? 28 : 32,
            Font = hasSubtitle ? DesktopTheme.TitleFont(12f) : DesktopTheme.TitleFont(14f),
            ForeColor = Color.FromArgb(20, 33, 61)
        });
        return header;
    }

    private static Panel CreateCardShell()
    {
        return DesktopSurfaceFactory.CreateCardShell();
    }

    private static Control CreateValueCard(string title, Label valueLabel)
    {
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = DesktopTheme.BodyFont(10f);
        valueLabel.ForeColor = DesktopTheme.TextPrimary;
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 0, 12, 12)
        };
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 22,
            Font = DesktopTheme.EmphasisFont(9.5f),
            ForeColor = DesktopTheme.TextSecondary
        });
        return panel;
    }

    private static void SetText(Label label, string value)
    {
        label.Text = string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static void RestoreGridSelection<TRow>(
        DataGridView grid,
        Func<TRow, Guid> keySelector,
        Guid? selectedId)
    {
        if (selectedId is null)
        {
            if (grid.Rows.Count > 0)
            {
                grid.Rows[0].Selected = true;
                grid.CurrentCell = grid.Rows[0].Cells[0];
            }

            return;
        }

        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.DataBoundItem is TRow data && keySelector(data) == selectedId.Value)
            {
                row.Selected = true;
                grid.CurrentCell = row.Cells[0];
                return;
            }
        }

        if (grid.Rows.Count > 0)
        {
            grid.Rows[0].Selected = true;
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }
    }

    private enum WorkspaceProcessFocus
    {
        Customers,
        Orders,
        Full
    }

    private sealed record CustomerGridRow(
        [property: Browsable(false)] Guid CustomerId,
        [property: DisplayName("Код")] string Code,
        [property: DisplayName("Клиент")] string Name,
        [property: DisplayName("Договор")] string Contract,
        [property: DisplayName("Менеджер")] string Manager,
        [property: DisplayName("Статус")] string Status);

    private sealed record CustomerOrderRow(
        [property: DisplayName("Заказ")] string Number,
        [property: DisplayName("Дата")] string Date,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Сумма")] string Total);

    private sealed record IncomingOrderRow(
        [property: Browsable(false)] Guid OrderId,
        [property: DisplayName("№ заказа")] string Number,
        [property: DisplayName("Клиент")] string Customer,
        [property: DisplayName("Дата заказа")] string Date,
        [property: DisplayName("Сумма")] string Total,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Срок отгрузки")] string DeliveryDate,
        [property: DisplayName("Действия")] string Actions);

    private sealed record OrderGridRow(
        [property: Browsable(false)] Guid OrderId,
        [property: DisplayName("Заказ")] string Number,
        [property: DisplayName("Дата")] string Date,
        [property: DisplayName("Клиент")] string Customer,
        [property: DisplayName("Склад")] string Warehouse,
        [property: DisplayName("Позиций")] int Positions,
        [property: DisplayName("На сумму")] string Total,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Менеджер")] string Manager);

    private sealed record OrderLineGridRow(
        [property: DisplayName("Код")] string Code,
        [property: DisplayName("Номенклатура")] string Item,
        [property: DisplayName("Ед.")] string Unit,
        [property: DisplayName("Кол-во")] decimal Quantity,
        [property: DisplayName("Цена")] string Price,
        [property: DisplayName("Сумма")] string Total);

    private sealed record RecentShipmentRow(
        [property: DisplayName("Отгрузка")] string Number,
        [property: DisplayName("Заказ")] string OrderNumber,
        [property: DisplayName("Клиент")] string Customer,
        [property: DisplayName("Дата")] string Date,
        [property: DisplayName("Статус")] string Status);

    private sealed record OrderStatusBreakdownSlice(string Status, int Count, Color Color);

    private sealed record OperationLogRow(
        [property: Browsable(false)] Guid OperationId,
        [property: DisplayName("Когда")] string LoggedAt,
        [property: DisplayName("Кто")] string Actor,
        [property: DisplayName("Сущность")] string EntityType,
        [property: DisplayName("Документ")] string EntityNumber,
        [property: DisplayName("Действие")] string Action,
        [property: DisplayName("Результат")] string Result,
        [property: DisplayName("Комментарий")] string Message);
}




