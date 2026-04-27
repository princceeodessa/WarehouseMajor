using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class PurchasingWorkspaceControl : UserControl
{
    private readonly SalesWorkspace _salesWorkspace;
    private readonly PurchasingOperationalWorkspaceStore _store;
    private readonly OperationalPurchasingWorkspace _workspace;
    private readonly BindingSource _supplierBindingSource = new();
    private readonly BindingSource _operationBindingSource = new();
    private readonly BindingSource _ordersQueueBindingSource = new();
    private readonly BindingSource _invoicesQueueBindingSource = new();
    private readonly BindingSource _receiptsQueueBindingSource = new();
    private readonly DocumentTabContext _ordersContext = new("Заказы поставщикам", "Заказы, которые формируются и согласуются уже в локальном контуре.");
    private readonly DocumentTabContext _invoicesContext = new("Счета поставщиков", "Входящие счета, оплата и переход в платежный контур.");
    private readonly DocumentTabContext _receiptsContext = new("Приемка", "Поступление и размещение товаров по складу без интерфейса 1С.");
    private readonly TextBox _supplierSearchTextBox = new();
    private readonly Label _supplierFilteredLabel = new();
    private readonly Label _noteLabel = new();
    private readonly Label _supplierCountValueLabel = new();
    private readonly Label _orderCountValueLabel = new();
    private readonly Label _invoiceCountValueLabel = new();
    private readonly Label _receiptCountValueLabel = new();
    private readonly Label _supplierNameValueLabel = new();
    private readonly Label _supplierCodeValueLabel = new();
    private readonly Label _supplierStatusValueLabel = new();
    private readonly Label _supplierTaxIdValueLabel = new();
    private readonly Label _supplierPhoneValueLabel = new();
    private readonly Label _supplierEmailValueLabel = new();
    private readonly Label _supplierContractValueLabel = new();
    private readonly Label _supplierSourceValueLabel = new();
    private readonly Label _ordersQueueCountLabel = new();
    private readonly Label _invoicesQueueCountLabel = new();
    private readonly Label _receiptsQueueCountLabel = new();
    private readonly DataGridView _supplierGrid = DesktopGridFactory.CreateGrid(Array.Empty<SupplierGridRow>());
    private readonly DataGridView _ordersQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());
    private readonly DataGridView _invoicesQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());
    private readonly DataGridView _receiptsQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());
    private readonly DataGridView _operationsGrid = DesktopGridFactory.CreateGrid(Array.Empty<OperationGridRow>());
    private readonly TabControl _tabs = DesktopSurfaceFactory.CreateTabControl();
    private readonly System.Windows.Forms.Timer _searchDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _persistDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _refreshDebounceTimer = new();
    private Action? _pendingSearchRefresh;
    private bool _refreshPendingWhileHidden;
    private bool _savePending;
    private bool _notifySalesWorkspacePending;

    public PurchasingWorkspaceControl(
        SalesWorkspace salesWorkspace,
        PurchasingOperationalWorkspaceStore? store = null,
        OperationalPurchasingWorkspace? workspace = null)
    {
        _salesWorkspace = salesWorkspace;
        _store = store ?? PurchasingOperationalWorkspaceStore.CreateDefault();
        _workspace = workspace ?? _store.LoadOrCreate(
            string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator) ? Environment.UserName : salesWorkspace.CurrentOperator,
            salesWorkspace);

        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        _supplierGrid.DataSource = _supplierBindingSource;
        _supplierGrid.SelectionChanged += (_, _) => RefreshSupplierDetails();
        _supplierGrid.DoubleClick += (_, _) => EditSelectedSupplier();
        _supplierSearchTextBox.TextChanged += (_, _) => ScheduleSearchRefresh(() => RefreshSupplierGrid());
        _supplierGrid.CellFormatting += HandleStatusCellFormatting;
        _ordersQueueGrid.CellFormatting += HandleStatusCellFormatting;
        _invoicesQueueGrid.CellFormatting += HandleStatusCellFormatting;
        _receiptsQueueGrid.CellFormatting += HandleStatusCellFormatting;
        _searchDebounceTimer.Interval = 180;
        _searchDebounceTimer.Tick += HandleSearchDebounceTick;
        _persistDebounceTimer.Interval = 750;
        _persistDebounceTimer.Tick += HandlePersistDebounceTick;
        _refreshDebounceTimer.Interval = 120;
        _refreshDebounceTimer.Tick += HandleRefreshDebounceTick;
        ConfigureQueueGrid(_ordersQueueGrid, _ordersQueueBindingSource, _ordersContext);
        ConfigureQueueGrid(_invoicesQueueGrid, _invoicesQueueBindingSource, _invoicesContext);
        ConfigureQueueGrid(_receiptsQueueGrid, _receiptsQueueBindingSource, _receiptsContext);

        ConfigureDocumentContext(_ordersContext);
        ConfigureDocumentContext(_invoicesContext);
        ConfigureDocumentContext(_receiptsContext);

        BuildLayout();
        RefreshAll();

        _workspace.Changed += HandleWorkspaceChanged;
        VisibleChanged += HandleVisibilityChanged;
        Disposed += (_, _) =>
        {
            FlushPendingSave();
            _workspace.Changed -= HandleWorkspaceChanged;
            VisibleChanged -= HandleVisibilityChanged;
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Tick -= HandleSearchDebounceTick;
            _searchDebounceTimer.Dispose();
            _persistDebounceTimer.Stop();
            _persistDebounceTimer.Tick -= HandlePersistDebounceTick;
            _persistDebounceTimer.Dispose();
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Tick -= HandleRefreshDebounceTick;
            _refreshDebounceTimer.Dispose();
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
        }
        SchedulePersist();
        ScheduleRefresh(notifySalesWorkspace: true);
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
        RunPendingRefresh();
    }

    private bool CanRefreshNow()
    {
        return !IsDisposed && IsHandleCreated && Visible && Parent is not null;
    }

    private void SchedulePersist()
    {
        _savePending = true;
        _persistDebounceTimer.Stop();
        _persistDebounceTimer.Start();
    }

    private void HandlePersistDebounceTick(object? sender, EventArgs e)
    {
        _persistDebounceTimer.Stop();
        FlushPendingSave();
    }

    private void ScheduleRefresh(bool notifySalesWorkspace = false)
    {
        if (notifySalesWorkspace)
        {
            _notifySalesWorkspacePending = true;
        }

        _refreshDebounceTimer.Stop();
        _refreshDebounceTimer.Start();
    }

    private void HandleRefreshDebounceTick(object? sender, EventArgs e)
    {
        _refreshDebounceTimer.Stop();
        RunPendingRefresh();
    }

    private void RunPendingRefresh()
    {
        if (!CanRefreshNow())
        {
            _refreshPendingWhileHidden = true;
            return;
        }

        RefreshAll();
        if (_notifySalesWorkspacePending)
        {
            _notifySalesWorkspacePending = false;
            _salesWorkspace.NotifyExternalChange();
        }
    }

    private void FlushPendingSave()
    {
        if (!_savePending)
        {
            return;
        }

        _savePending = false;
        _store.Save(_workspace);
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateOperatorQuickActions(), 0, 1);
        root.Controls.Add(CreateSummaryNote(), 0, 2);
        root.Controls.Add(CreateSummaryCards(), 0, 3);
        root.Controls.Add(CreateTabs(), 0, 4);

        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Закупки: поставщики, заказы, счета и приемка работают в локальном desktop-контуре.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Закупки и приемка",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
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
        flow.Controls.Add(DesktopSurfaceFactory.CreateInfoChip("Смена закупок", DesktopTheme.PrimarySoft, DesktopTheme.SidebarButtonActiveText));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый поставщик", (_, _) => CreateSupplier(), DesktopButtonTone.Primary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новый заказ", (_, _) => CreateOrder(), DesktopButtonTone.Secondary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Счет из заказа", (_, _) => CreateInvoiceFromSelectedOrder(), DesktopButtonTone.Secondary));
        flow.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Приемка", (_, _) => CreateReceiptFromSelectedOrder(), DesktopButtonTone.Ghost));

        shell.Controls.Add(flow);
        return shell;
    }

    private Control CreateSummaryNote()
    {
        _noteLabel.Dock = DockStyle.Top;
        _noteLabel.Height = 42;
        _noteLabel.Font = new Font("Segoe UI", 9.2f);
        _noteLabel.ForeColor = Color.FromArgb(97, 88, 80);

        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Dock = DockStyle.Top;
        panel.Height = 54;
        panel.Padding = new Padding(12, 10, 12, 0);
        panel.BackColor = Color.FromArgb(255, 250, 241);
        panel.Margin = new Padding(0, 0, 0, 12);
        panel.Controls.Add(_noteLabel);
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
        flow.Controls.Add(CreateSummaryCard("Поставщики", "Локальные карточки и импортированная база.", _supplierCountValueLabel, Color.FromArgb(79, 174, 92)));
        flow.Controls.Add(CreateSummaryCard("Заказы", "Закупки в работе и ожидаемые поставки.", _orderCountValueLabel, Color.FromArgb(78, 160, 190)));
        flow.Controls.Add(CreateSummaryCard("Счета", "Полученные и оплаченные счета поставщиков.", _invoiceCountValueLabel, Color.FromArgb(201, 134, 64)));
        flow.Controls.Add(CreateSummaryCard("Приемка", "Поступление и размещение товара.", _receiptCountValueLabel, Color.FromArgb(196, 92, 83)));
        return flow;
    }

    private static Control CreateSummaryCard(string title, string hint, Label valueLabel, Color accentColor)
    {
        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 36;
        valueLabel.Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(43, 39, 34);

        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Width = 244;
        panel.Height = 96;
        panel.Margin = new Padding(0, 0, 12, 12);
        panel.Padding = new Padding(14, 12, 14, 12);
        var accent = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accentColor };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        content.Controls.Add(new Label { Text = hint, Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(112, 103, 92) });
        content.Controls.Add(valueLabel);
        content.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(68, 61, 53) });
        panel.Controls.Add(content);
        panel.Controls.Add(accent);
        return panel;
    }

    private Control CreateTabs()
    {
        _tabs.TabPages.Clear();
        var tabs = _tabs;
        tabs.TabPages.Add(CreateSuppliersTab());
        tabs.TabPages.Add(CreateDocumentTab("Заказы", _ordersContext, CreateOrdersToolbar()));
        tabs.TabPages.Add(CreateDocumentTab("Счета", _invoicesContext, CreateInvoicesToolbar()));
        tabs.TabPages.Add(CreateDocumentTab("Приемка", _receiptsContext, CreateReceiptsToolbar()));
        tabs.TabPages.Add(CreateOperationsTab());
        return tabs;
    }

    private TabPage CreateSuppliersTab()
    {
        _supplierSearchTextBox.Width = 280;
        _supplierSearchTextBox.Font = new Font("Segoe UI", 10f);
        _supplierSearchTextBox.PlaceholderText = "Поиск поставщика";
        _supplierSearchTextBox.Margin = new Padding(0, 2, 8, 8);
        ConfigureDigestLabel(_supplierFilteredLabel);

        PrepareDetailLabel(_supplierNameValueLabel);
        PrepareDetailLabel(_supplierCodeValueLabel);
        PrepareDetailLabel(_supplierStatusValueLabel);
        PrepareDetailLabel(_supplierTaxIdValueLabel);
        PrepareDetailLabel(_supplierPhoneValueLabel);
        PrepareDetailLabel(_supplierEmailValueLabel);
        PrepareDetailLabel(_supplierContractValueLabel);
        PrepareDetailLabel(_supplierSourceValueLabel);

        var toolbar = DesktopSurfaceFactory.CreateToolbarStrip(bottomPadding: 10);
        toolbar.Controls.Add(_supplierSearchTextBox);
        toolbar.Controls.Add(_supplierFilteredLabel);
        toolbar.Controls.Add(CreateActionButton("Новый поставщик", (_, _) => CreateSupplier()));
        toolbar.Controls.Add(CreateActionButton("Изменить поставщика", (_, _) => EditSelectedSupplier()));
        toolbar.Controls.Add(CreateActionButton("Новый заказ", (_, _) => CreateOrderForSelectedSupplier()));

        var details = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = DesktopTheme.Surface,
            Padding = new Padding(14)
        };
        details.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        details.Controls.Add(new Label { Text = "Карточка поставщика", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(53, 47, 41) }, 0, 0);
        details.Controls.Add(CreateDetailGrid(
            ("Поставщик", _supplierNameValueLabel),
            ("Код", _supplierCodeValueLabel),
            ("Статус", _supplierStatusValueLabel),
            ("ИНН / КПП", _supplierTaxIdValueLabel),
            ("Телефон", _supplierPhoneValueLabel),
            ("Email", _supplierEmailValueLabel),
            ("Договор", _supplierContractValueLabel),
            ("Источник", _supplierSourceValueLabel)), 0, 1);

        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        body.Controls.Add(CreateGridShell("Реестр поставщиков", "Поставщики, с которыми закупочный контур работает автономно.", _supplierGrid), 0, 0);
        var rightColumn = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        rightColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        rightColumn.Controls.Add(details, 0, 0);
        rightColumn.Controls.Add(CreateWorkspaceQueuesPanel(), 0, 1);
        body.Controls.Add(rightColumn, 1, 0);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(body, 0, 1);

        var tab = new TabPage("Поставщики") { Padding = new Padding(10) };
        tab.Controls.Add(root);
        return tab;
    }

    private Control CreateWorkspaceQueuesPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 33));

        root.Controls.Add(CreateQueueCard("Заказы", "То, что снабженцу нужно доводить до поставки.", _ordersQueueCountLabel, _ordersQueueGrid, (_, _) => OpenQueueTab(1, _ordersContext)), 0, 0);
        root.Controls.Add(CreateQueueCard("Счета", "Входящие счета на контроле, оплате и сверке.", _invoicesQueueCountLabel, _invoicesQueueGrid, (_, _) => OpenQueueTab(2, _invoicesContext)), 0, 1);
        root.Controls.Add(CreateQueueCard("Приемка", "Поступления, которые нужно принять и разместить по складу.", _receiptsQueueCountLabel, _receiptsQueueGrid, (_, _) => OpenQueueTab(3, _receiptsContext)), 0, 2);
        return root;
    }

    private Control CreateQueueCard(string title, string subtitle, Label countLabel, DataGridView grid, EventHandler openHandler)
    {
        var shell = DesktopSurfaceFactory.CreateCardShell();
        shell.Padding = new Padding(14);

        countLabel.AutoSize = true;
        countLabel.Dock = DockStyle.Right;
        countLabel.Font = DesktopTheme.EmphasisFont(9.4f);
        countLabel.ForeColor = DesktopTheme.SidebarButtonActiveText;

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52
        };
        header.Controls.Add(countLabel);
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 20,
            Font = DesktopTheme.BodyFont(8.8f),
            ForeColor = DesktopTheme.TextSecondary
        });
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 24,
            Font = DesktopTheme.TitleFont(11f),
            ForeColor = DesktopTheme.TextPrimary
        });

        var openButton = DesktopSurfaceFactory.CreateActionButton("Открыть", openHandler, DesktopButtonTone.Ghost, new Padding(0));
        openButton.Dock = DockStyle.Right;

        var footer = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34
        };
        footer.Controls.Add(openButton);

        shell.Controls.Add(grid);
        shell.Controls.Add(footer);
        shell.Controls.Add(header);
        return shell;
    }

    private TabPage CreateDocumentTab(string title, DocumentTabContext context, Control toolbar)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 700 };
        split.Panel1.Controls.Add(CreateGridShell("Реестр документов", context.Summary, context.RecordGrid));
        split.Panel2.Controls.Add(CreateDocumentDetails(context));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(split, 0, 1);

        var tab = new TabPage(title) { Padding = new Padding(10) };
        tab.Controls.Add(root);
        return tab;
    }

    private TabPage CreateOperationsTab()
    {
        _operationsGrid.DataSource = _operationBindingSource;

        var card = CreateGridShell("Журнал операций", "Кто и когда создал, согласовал, оплатил или принял документ.", _operationsGrid);
        var tab = new TabPage("Журнал") { Padding = new Padding(10) };
        tab.Controls.Add(card);
        return tab;
    }

    private Control CreateOrdersToolbar()
    {
        _ordersContext.SearchTextBox.Width = 240;
        _ordersContext.SearchTextBox.Font = new Font("Segoe UI", 10f);
        _ordersContext.SearchTextBox.PlaceholderText = "Поиск заказа";

        var panel = CreateToolbarBase(_ordersContext);
        panel.Controls.Add(CreateActionButton("Печать заказа", (_, _) => PrintSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Новый заказ", (_, _) => CreateOrder()));
        panel.Controls.Add(CreateActionButton("Изменить заказ", (_, _) => EditSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Согласовать", (_, _) => ApproveSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Разместить", (_, _) => PlaceSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Сформировать счет", (_, _) => CreateInvoiceFromSelectedOrder()));
        panel.Controls.Add(CreateActionButton("Сформировать приемку", (_, _) => CreateReceiptFromSelectedOrder()));
        return panel;
    }

    private Control CreateInvoicesToolbar()
    {
        _invoicesContext.SearchTextBox.Width = 240;
        _invoicesContext.SearchTextBox.Font = new Font("Segoe UI", 10f);
        _invoicesContext.SearchTextBox.PlaceholderText = "Поиск счета";

        var panel = CreateToolbarBase(_invoicesContext);
        panel.Controls.Add(CreateActionButton("Печать счета", (_, _) => PrintSelectedInvoice()));
        panel.Controls.Add(CreateActionButton("Получен", (_, _) => MarkSelectedInvoiceReceived()));
        panel.Controls.Add(CreateActionButton("К оплате", (_, _) => MarkSelectedInvoicePayable()));
        panel.Controls.Add(CreateActionButton("Оплачен", (_, _) => MarkSelectedInvoicePaid()));
        return panel;
    }

    private Control CreateReceiptsToolbar()
    {
        _receiptsContext.SearchTextBox.Width = 240;
        _receiptsContext.SearchTextBox.Font = new Font("Segoe UI", 10f);
        _receiptsContext.SearchTextBox.PlaceholderText = "Поиск приемки";

        var panel = CreateToolbarBase(_receiptsContext);
        panel.Controls.Add(CreateActionButton("Печать приемки", (_, _) => PrintSelectedReceipt()));
        panel.Controls.Add(CreateActionButton("Принять", (_, _) => ReceiveSelectedReceipt()));
        panel.Controls.Add(CreateActionButton("Разместить", (_, _) => PlaceSelectedReceipt()));
        return panel;
    }

    private static FlowLayoutPanel CreateToolbarBase(DocumentTabContext context)
    {
        context.SearchTextBox.Margin = new Padding(0, 2, 8, 8);
        context.CountLabel.Margin = new Padding(0, 7, 8, 8);
        var panel = DesktopSurfaceFactory.CreateToolbarStrip(bottomPadding: 10);
        panel.Controls.Add(context.SearchTextBox);
        panel.Controls.Add(context.CountLabel);
        return panel;
    }

    private Control CreateDocumentDetails(DocumentTabContext context)
    {
        PrepareDetailLabel(context.NumberLabel);
        PrepareDetailLabel(context.DateLabel);
        PrepareDetailLabel(context.SupplierLabel);
        PrepareDetailLabel(context.StatusLabel);
        PrepareDetailLabel(context.WarehouseLabel);
        PrepareDetailLabel(context.ContractLabel);
        PrepareDetailLabel(context.LinkLabel);
        PrepareDetailLabel(context.AmountLabel);
        PrepareDetailLabel(context.SourceLabel);
        PrepareDetailLabel(context.CommentLabel);

        var header = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true, BackColor = Color.White, Padding = new Padding(14) };
        header.Controls.Add(new Label { Text = "Карточка документа", Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(53, 47, 41) }, 0, 0);
        header.Controls.Add(CreateDetailGrid(
            ("Номер", context.NumberLabel),
            ("Дата", context.DateLabel),
            ("Поставщик", context.SupplierLabel),
            ("Статус", context.StatusLabel),
            ("Склад", context.WarehouseLabel),
            ("Договор", context.ContractLabel),
            ("Основание", context.LinkLabel),
            ("Сумма", context.AmountLabel),
            ("Источник", context.SourceLabel),
            ("Комментарий", context.CommentLabel)), 0, 1);

        var lower = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220, BackColor = Color.White };
        lower.Panel1.Padding = new Padding(14, 0, 14, 14);
        lower.Panel1.Controls.Add(CreateGridShell("Строки документа", "Товары, цены и планируемые даты поставки.", context.LineGrid));
        lower.Panel2.Padding = new Padding(14, 0, 14, 14);
        lower.Panel2.Controls.Add(CreateGridShell("Поля 1С", "Импортированные поля-источники для контроля миграции.", context.FieldGrid));

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(lower, 0, 1);
        return root;
    }

    private static Control CreateGridShell(string title, string summary, Control grid)
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.White, Padding = new Padding(14) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Controls.Add(new Label { Text = summary, Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(107, 98, 88) });
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(53, 47, 41) });
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(grid, 0, 1);
        return root;
    }

    private static Control CreateDetailGrid(params (string Caption, Label ValueLabel)[] items)
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2 };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var i = 0; i < items.Length; i++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            table.Controls.Add(new Label { Text = items[i].Caption, AutoSize = true, Font = new Font("Segoe UI Semibold", 9.1f, FontStyle.Bold), ForeColor = Color.FromArgb(88, 79, 70), Margin = new Padding(0, 0, 8, 10) }, 0, i);
            table.Controls.Add(items[i].ValueLabel, 1, i);
        }

        return table;
    }

    private static void PrepareDetailLabel(Label label)
    {
        label.Dock = DockStyle.Top;
        label.AutoSize = true;
        label.MaximumSize = new Size(420, 0);
        label.Font = new Font("Segoe UI", 9.2f);
        label.ForeColor = Color.FromArgb(49, 44, 38);
        label.Margin = new Padding(0, 0, 0, 10);
    }

    private static Button CreateActionButton(string text, EventHandler handler)
    {
        return DesktopSurfaceFactory.CreateActionButton(text, handler, DesktopButtonTone.Secondary);
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

    private void ConfigureQueueGrid(DataGridView grid, BindingSource bindingSource, DocumentTabContext context)
    {
        grid.DataSource = bindingSource;
        grid.DoubleClick += (_, _) =>
        {
            var selectedId = (grid.CurrentRow?.DataBoundItem as QueueDocumentRow)?.DocumentId;
            OpenQueueTab(ResolveQueueTabIndex(context), context, selectedId);
        };
    }

    private int ResolveQueueTabIndex(DocumentTabContext context)
    {
        if (ReferenceEquals(context, _ordersContext))
        {
            return 1;
        }

        if (ReferenceEquals(context, _invoicesContext))
        {
            return 2;
        }

        return 3;
    }

    private void OpenQueueTab(int tabIndex, DocumentTabContext context, Guid? selectedId = null)
    {
        if (selectedId is not null)
        {
            RefreshDocumentGrid(context, selectedId: selectedId);
        }

        if (_tabs.TabPages.Count > tabIndex)
        {
            _tabs.SelectedIndex = tabIndex;
        }
    }

    private void ConfigureDocumentContext(DocumentTabContext context)
    {
        context.CountLabel.AutoSize = true;
        context.CountLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        context.CountLabel.ForeColor = Color.FromArgb(82, 74, 66);
        context.CountLabel.Margin = new Padding(10, 9, 0, 0);
        context.RecordGrid.DataSource = context.RecordBindingSource;
        context.LineGrid.DataSource = context.LineBindingSource;
        context.FieldGrid.DataSource = context.FieldBindingSource;
        context.RecordGrid.SelectionChanged += (_, _) => RefreshDocumentDetails(context);
        context.RecordGrid.CellFormatting += HandleStatusCellFormatting;
        context.SearchTextBox.TextChanged += (_, _) => ScheduleSearchRefresh(() => RefreshDocumentGrid(context));
    }

    private void ScheduleSearchRefresh(Action refreshAction)
    {
        _pendingSearchRefresh = refreshAction;
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void HandleSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        var refreshAction = _pendingSearchRefresh;
        _pendingSearchRefresh = null;
        refreshAction?.Invoke();
    }

    private void RefreshAll()
    {
        SuspendLayout();
        try
        {
        _searchDebounceTimer.Stop();
        _pendingSearchRefresh = null;
        _noteLabel.Text = $"Закупочный контур работает автономно. Локально сохранены поставщики, заказы, входящие счета, приемка и журнал действий пользователя {_workspace.CurrentOperator}.";

        _supplierCountValueLabel.Text = _workspace.Suppliers.Count.ToString("N0");
        _orderCountValueLabel.Text = _workspace.PurchaseOrders.Count.ToString("N0");
        _invoiceCountValueLabel.Text = _workspace.SupplierInvoices.Count.ToString("N0");
        _receiptCountValueLabel.Text = _workspace.PurchaseReceipts.Count.ToString("N0");

        RefreshSupplierGrid();
        RefreshDocumentGrid(_ordersContext, _workspace.PurchaseOrders);
        RefreshDocumentGrid(_invoicesContext, _workspace.SupplierInvoices);
        RefreshDocumentGrid(_receiptsContext, _workspace.PurchaseReceipts);
        RefreshWorkspaceQueues();
        RefreshOperationsLog();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void RefreshSupplierGrid(Guid? selectedSupplierId = null)
    {
        var currentId = selectedSupplierId ?? GetSelectedSupplierId();
        var search = _supplierSearchTextBox.Text.Trim();
        _supplierBindingSource.DataSource = _workspace.Suppliers
            .Where(item => string.IsNullOrWhiteSpace(search) || MatchesSupplierSearch(item, search))
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(item => new SupplierGridRow(item))
            .ToArray();
        _supplierFilteredLabel.Text = $"Показано: {_supplierBindingSource.Count:N0} из {_workspace.Suppliers.Count:N0}";

        RestoreGridSelection<SupplierGridRow>(_supplierGrid, row => row.SupplierId, currentId);
        RefreshSupplierDetails();
    }

    private void RefreshSupplierDetails()
    {
        var record = GetSelectedSupplier();
        _supplierNameValueLabel.Text = record?.Name ?? "—";
        _supplierCodeValueLabel.Text = record?.Code ?? "—";
        _supplierStatusValueLabel.Text = record?.Status ?? "—";
        _supplierTaxIdValueLabel.Text = record?.TaxId ?? "—";
        _supplierPhoneValueLabel.Text = record?.Phone ?? "—";
        _supplierEmailValueLabel.Text = record?.Email ?? "—";
        _supplierContractValueLabel.Text = record?.Contract ?? "—";
        _supplierSourceValueLabel.Text = record?.SourceLabel ?? "—";
    }

    private void RefreshDocumentGrid(DocumentTabContext context, IEnumerable<OperationalPurchasingDocumentRecord>? source = null, Guid? selectedId = null)
    {
        if (source is not null)
        {
            context.Records = source.ToArray();
        }

        var currentId = selectedId ?? GetSelectedDocumentId(context);
        var search = context.SearchTextBox.Text.Trim();
        var rows = context.Records
            .Where(item => string.IsNullOrWhiteSpace(search) || MatchesDocumentSearch(item, search))
            .OrderByDescending(item => item.DocumentDate)
            .ThenByDescending(item => item.Number)
            .Select(item => new DocumentGridRow(item))
            .ToArray();
        context.RecordBindingSource.DataSource = rows;

        context.CountLabel.Text = $"Показано: {rows.Length:N0} из {context.Records.Count:N0}";
        RestoreGridSelection<DocumentGridRow>(context.RecordGrid, row => row.DocumentId, currentId);
        RefreshDocumentDetails(context);
    }

    private void RefreshDocumentDetails(DocumentTabContext context)
    {
        var record = GetSelectedDocument(context);
        context.NumberLabel.Text = record?.Number ?? "—";
        context.DateLabel.Text = record?.DocumentDate.ToString("dd.MM.yyyy") ?? "—";
        context.SupplierLabel.Text = record?.SupplierName ?? "—";
        context.StatusLabel.Text = record?.Status ?? "—";
        context.WarehouseLabel.Text = record?.Warehouse ?? "—";
        context.ContractLabel.Text = record?.Contract ?? "—";
        context.LinkLabel.Text = record?.RelatedOrderNumber ?? "—";
        context.AmountLabel.Text = record is null ? "—" : $"{record.TotalAmount:N2} ₽";
        context.SourceLabel.Text = record?.SourceLabel ?? "—";
        context.CommentLabel.Text = string.IsNullOrWhiteSpace(record?.Comment) ? "—" : record!.Comment;
        context.LineBindingSource.DataSource = record?.Lines.Select(item => new DocumentLineGridRow(item)).ToArray() ?? Array.Empty<DocumentLineGridRow>();
        context.FieldBindingSource.DataSource = record?.Fields.Select(item => new FieldGridRow(item)).ToArray() ?? Array.Empty<FieldGridRow>();
    }

    private void RefreshWorkspaceQueues()
    {
        _ordersQueueBindingSource.DataSource = _ordersContext.Records
            .Take(6)
            .Select(item => new QueueDocumentRow(item))
            .ToArray();
        _invoicesQueueBindingSource.DataSource = _invoicesContext.Records
            .Take(6)
            .Select(item => new QueueDocumentRow(item))
            .ToArray();
        _receiptsQueueBindingSource.DataSource = _receiptsContext.Records
            .Take(6)
            .Select(item => new QueueDocumentRow(item))
            .ToArray();

        _ordersQueueCountLabel.Text = $"{_ordersContext.Records.Count:N0}";
        _invoicesQueueCountLabel.Text = $"{_invoicesContext.Records.Count:N0}";
        _receiptsQueueCountLabel.Text = $"{_receiptsContext.Records.Count:N0}";
    }

    private void RefreshOperationsLog()
    {
        _operationBindingSource.DataSource = _workspace.OperationLog
            .Select(item => new OperationGridRow(item))
            .ToArray();
    }

    private void CreateSupplier()
    {
        using var form = new SupplierEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultSupplier is null)
        {
            return;
        }

        _workspace.AddSupplier(form.ResultSupplier);
        RefreshSupplierGrid(form.ResultSupplier.Id);
    }

    private void EditSelectedSupplier()
    {
        var supplier = GetSelectedSupplier();
        if (supplier is null)
        {
            ShowSelectionWarning("Сначала выберите поставщика.");
            return;
        }

        using var form = new SupplierEditorForm(_workspace, supplier);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultSupplier is null)
        {
            return;
        }

        _workspace.UpdateSupplier(form.ResultSupplier);
        RefreshSupplierGrid(form.ResultSupplier.Id);
        RefreshDocumentGrid(_ordersContext);
        RefreshDocumentGrid(_invoicesContext);
        RefreshDocumentGrid(_receiptsContext);
    }

    private void CreateOrder()
    {
        using var form = new PurchaseOrderEditorForm(_workspace, null, GetSelectedSupplierId());
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultDocument is null)
        {
            return;
        }

        _workspace.AddPurchaseOrder(form.ResultDocument);
        RefreshDocumentGrid(_ordersContext, selectedId: form.ResultDocument.Id);
        RefreshSupplierGrid(form.ResultDocument.SupplierId);
    }

    private void CreateOrderForSelectedSupplier()
    {
        CreateOrder();
    }

    private void EditSelectedOrder()
    {
        var order = GetSelectedDocument(_ordersContext);
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ поставщику.");
            return;
        }

        using var form = new PurchaseOrderEditorForm(_workspace, order);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultDocument is null)
        {
            return;
        }

        _workspace.UpdatePurchaseOrder(form.ResultDocument);
        RefreshDocumentGrid(_ordersContext, selectedId: form.ResultDocument.Id);
        RefreshSupplierGrid(form.ResultDocument.SupplierId);
        RefreshDocumentGrid(_invoicesContext);
        RefreshDocumentGrid(_receiptsContext);
    }

    private void ApproveSelectedOrder()
    {
        var order = GetSelectedDocument(_ordersContext);
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ поставщику.");
            return;
        }

        ShowWorkflowResult(_workspace.ApprovePurchaseOrder(order.Id), "Закупки");
        RefreshDocumentGrid(_ordersContext, selectedId: order.Id);
    }

    private void PlaceSelectedOrder()
    {
        var order = GetSelectedDocument(_ordersContext);
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ поставщику.");
            return;
        }

        ShowWorkflowResult(_workspace.PlacePurchaseOrder(order.Id), "Закупки");
        RefreshDocumentGrid(_ordersContext, selectedId: order.Id);
    }

    private void CreateInvoiceFromSelectedOrder()
    {
        var order = GetSelectedDocument(_ordersContext);
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, из которого нужно создать счет поставщика.");
            return;
        }

        var document = _workspace.CreateSupplierInvoiceDraftFromOrder(order.Id);
        _workspace.AddSupplierInvoice(document);
        RefreshDocumentGrid(_ordersContext, selectedId: order.Id);
        RefreshDocumentGrid(_invoicesContext, selectedId: document.Id);
    }

    private void CreateReceiptFromSelectedOrder()
    {
        var order = GetSelectedDocument(_ordersContext);
        if (order is null)
        {
            ShowSelectionWarning("Сначала выберите заказ, из которого нужно создать приемку.");
            return;
        }

        var document = _workspace.CreateReceiptDraftFromOrder(order.Id);
        _workspace.AddPurchaseReceipt(document);
        RefreshDocumentGrid(_ordersContext, selectedId: order.Id);
        RefreshDocumentGrid(_receiptsContext, selectedId: document.Id);
    }

    private void PrintSelectedOrder()
    {
        var document = GetSelectedDocument(_ordersContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите заказ поставщику.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать заказа {document.Number}",
            OperationalDocumentPrintComposer.BuildPurchaseOrderHtml(document));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void MarkSelectedInvoiceReceived()
    {
        var document = GetSelectedDocument(_invoicesContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите счет поставщика.");
            return;
        }

        ShowWorkflowResult(_workspace.MarkSupplierInvoiceReceived(document.Id), "Счета поставщиков");
        RefreshDocumentGrid(_ordersContext);
        RefreshDocumentGrid(_invoicesContext, selectedId: document.Id);
    }

    private void PrintSelectedInvoice()
    {
        var document = GetSelectedDocument(_invoicesContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите счет поставщика.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать счета {document.Number}",
            OperationalDocumentPrintComposer.BuildSupplierInvoiceHtml(document));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void MarkSelectedInvoicePayable()
    {
        var document = GetSelectedDocument(_invoicesContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите счет поставщика.");
            return;
        }

        ShowWorkflowResult(_workspace.MarkSupplierInvoicePayable(document.Id), "Счета поставщиков");
        RefreshDocumentGrid(_invoicesContext, selectedId: document.Id);
    }

    private void MarkSelectedInvoicePaid()
    {
        var document = GetSelectedDocument(_invoicesContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите счет поставщика.");
            return;
        }

        ShowWorkflowResult(_workspace.MarkSupplierInvoicePaid(document.Id), "Счета поставщиков");
        RefreshDocumentGrid(_invoicesContext, selectedId: document.Id);
    }

    private void ReceiveSelectedReceipt()
    {
        var document = GetSelectedDocument(_receiptsContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите приемку.");
            return;
        }

        ShowWorkflowResult(_workspace.ReceivePurchaseReceipt(document.Id), "Приемка");
        RefreshDocumentGrid(_ordersContext);
        RefreshDocumentGrid(_receiptsContext, selectedId: document.Id);
    }

    private void PrintSelectedReceipt()
    {
        var document = GetSelectedDocument(_receiptsContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите приемку.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать приемки {document.Number}",
            OperationalDocumentPrintComposer.BuildPurchaseReceiptHtml(document));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void PlaceSelectedReceipt()
    {
        var document = GetSelectedDocument(_receiptsContext);
        if (document is null)
        {
            ShowSelectionWarning("Сначала выберите приемку.");
            return;
        }

        ShowWorkflowResult(_workspace.PlacePurchaseReceipt(document.Id), "Приемка");
        RefreshDocumentGrid(_ordersContext);
        RefreshDocumentGrid(_receiptsContext, selectedId: document.Id);
    }

    private void ShowWorkflowResult(PurchasingWorkflowActionResult result, string caption)
    {
        MessageBox.Show(
            FindForm(),
            string.IsNullOrWhiteSpace(result.Detail) ? result.Message : $"{result.Message}{Environment.NewLine}{Environment.NewLine}{result.Detail}",
            caption,
            MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void ShowSelectionWarning(string message)
    {
        MessageBox.Show(FindForm(), message, "Закупки", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private OperationalPurchasingSupplierRecord? GetSelectedSupplier()
    {
        return _supplierGrid.CurrentRow?.DataBoundItem is SupplierGridRow row
            ? _workspace.Suppliers.FirstOrDefault(item => item.Id == row.SupplierId)
            : null;
    }

    private Guid? GetSelectedSupplierId()
    {
        return _supplierGrid.CurrentRow?.DataBoundItem is SupplierGridRow row ? row.SupplierId : null;
    }

    private OperationalPurchasingDocumentRecord? GetSelectedDocument(DocumentTabContext context)
    {
        return context.RecordGrid.CurrentRow?.DataBoundItem is DocumentGridRow row
            ? context.Records.FirstOrDefault(item => item.Id == row.DocumentId)
            : null;
    }

    private Guid? GetSelectedDocumentId(DocumentTabContext context)
    {
        return context.RecordGrid.CurrentRow?.DataBoundItem is DocumentGridRow row ? row.DocumentId : null;
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

        if (status.Contains("ошиб", StringComparison.OrdinalIgnoreCase)
            || status.Contains("error", StringComparison.OrdinalIgnoreCase)
            || status.Contains("отмен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(251, 231, 227);
            style.ForeColor = DesktopTheme.Danger;
            return;
        }

        if (status.Contains("архив", StringComparison.OrdinalIgnoreCase)
            || status.Contains("закры", StringComparison.OrdinalIgnoreCase)
            || status.Contains("исполн", StringComparison.OrdinalIgnoreCase)
            || status.Contains("оплачен", StringComparison.OrdinalIgnoreCase)
            || status.Contains("complete", StringComparison.OrdinalIgnoreCase)
            || status.Contains("paid", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = DesktopTheme.SurfaceMuted;
            style.ForeColor = DesktopTheme.TextMuted;
            return;
        }

        if (status.Contains("чернов", StringComparison.OrdinalIgnoreCase)
            || status.Contains("нов", StringComparison.OrdinalIgnoreCase)
            || status.Contains("соглас", StringComparison.OrdinalIgnoreCase)
            || status.Contains("к оплате", StringComparison.OrdinalIgnoreCase)
            || status.Contains("draft", StringComparison.OrdinalIgnoreCase)
            || status.Contains("new", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = DesktopTheme.PrimarySoft;
            style.ForeColor = DesktopTheme.SidebarButtonActiveText;
            return;
        }

        style.BackColor = DesktopTheme.InfoSoft;
        style.ForeColor = DesktopTheme.Info;
    }

    private static bool MatchesSupplierSearch(OperationalPurchasingSupplierRecord record, string search)
    {
        return record.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.TaxId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Contract.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Status.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDocumentSearch(OperationalPurchasingDocumentRecord record, string search)
    {
        return record.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.SupplierName.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Warehouse.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.RelatedOrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Comment.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static void RestoreGridSelection<TRow>(DataGridView grid, Func<TRow, Guid> keySelector, Guid? selectedId)
    {
        if (grid.Rows.Count == 0)
        {
            return;
        }

        if (selectedId is not null)
        {
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

        grid.Rows[0].Selected = true;
        grid.CurrentCell = grid.Rows[0].Cells[0];
    }

    private sealed class DocumentTabContext
    {
        public DocumentTabContext(string title, string summary)
        {
            Title = title;
            Summary = summary;
        }

        public string Title { get; }

        public string Summary { get; }

        public IReadOnlyList<OperationalPurchasingDocumentRecord> Records { get; set; } = Array.Empty<OperationalPurchasingDocumentRecord>();

        public TextBox SearchTextBox { get; } = new();

        public Label CountLabel { get; } = new();

        public Label NumberLabel { get; } = new();

        public Label DateLabel { get; } = new();

        public Label SupplierLabel { get; } = new();

        public Label StatusLabel { get; } = new();

        public Label WarehouseLabel { get; } = new();

        public Label ContractLabel { get; } = new();

        public Label LinkLabel { get; } = new();

        public Label AmountLabel { get; } = new();

        public Label SourceLabel { get; } = new();

        public Label CommentLabel { get; } = new();

        public BindingSource RecordBindingSource { get; } = new();

        public BindingSource LineBindingSource { get; } = new();

        public BindingSource FieldBindingSource { get; } = new();

        public DataGridView RecordGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<DocumentGridRow>());

        public DataGridView LineGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<DocumentLineGridRow>());

        public DataGridView FieldGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<FieldGridRow>());
    }

    private sealed record SupplierGridRow(
        [property: Browsable(false)] Guid SupplierId,
        [property: DisplayName("Поставщик")] string Name,
        [property: DisplayName("Код")] string Code,
        [property: DisplayName("ИНН / КПП")] string TaxId,
        [property: DisplayName("Договор")] string Contract,
        [property: DisplayName("Статус")] string Status)
    {
        public SupplierGridRow(OperationalPurchasingSupplierRecord record)
            : this(record.Id, record.Name, record.Code, record.TaxId, record.Contract, record.Status)
        {
        }
    }

    private sealed record DocumentGridRow(
        [property: Browsable(false)] Guid DocumentId,
        [property: DisplayName("Документ")] string Number,
        [property: DisplayName("Дата")] string Date,
        [property: DisplayName("Поставщик")] string Supplier,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Склад")] string Warehouse,
        [property: DisplayName("Основание")] string RelatedOrder,
        [property: DisplayName("Сумма")] decimal Amount)
    {
        public DocumentGridRow(OperationalPurchasingDocumentRecord record)
            : this(record.Id, record.Number, record.DocumentDate.ToString("dd.MM.yyyy"), record.SupplierName, record.Status, record.Warehouse, record.RelatedOrderNumber, record.TotalAmount)
        {
        }
    }

    private sealed record DocumentLineGridRow(
        [property: DisplayName("Код")] string ItemCode,
        [property: DisplayName("Номенклатура")] string ItemName,
        [property: DisplayName("Ед.")] string Unit,
        [property: DisplayName("Количество")] decimal Quantity,
        [property: DisplayName("Цена")] decimal Price,
        [property: DisplayName("Сумма")] decimal Amount,
        [property: DisplayName("План")] string PlannedDate)
    {
        public DocumentLineGridRow(OperationalPurchasingLineRecord record)
            : this(record.ItemCode, record.ItemName, record.Unit, record.Quantity, record.Price, record.Amount, record.PlannedDate?.ToString("dd.MM.yyyy") ?? string.Empty)
        {
        }
    }

    private sealed record FieldGridRow(
        [property: DisplayName("Поле")] string Name,
        [property: DisplayName("Значение")] string Value,
        [property: DisplayName("Raw")] string Raw)
    {
        public FieldGridRow(OneCFieldValue field)
            : this(field.Name, field.DisplayValue, field.RawValue)
        {
        }
    }

    private sealed record QueueDocumentRow(
        [property: Browsable(false)] Guid DocumentId,
        [property: DisplayName("Документ")] string Number,
        [property: DisplayName("Дата")] string Date,
        [property: DisplayName("Статус")] string Status)
    {
        public QueueDocumentRow(OperationalPurchasingDocumentRecord record)
            : this(record.Id, record.Number, record.DocumentDate.ToString("dd.MM"), record.Status)
        {
        }
    }

    private sealed record OperationGridRow(
        [property: DisplayName("Когда")] string LoggedAt,
        [property: DisplayName("Кто")] string Actor,
        [property: DisplayName("Сущность")] string EntityType,
        [property: DisplayName("Документ")] string EntityNumber,
        [property: DisplayName("Действие")] string Action,
        [property: DisplayName("Результат")] string Result,
        [property: DisplayName("Комментарий")] string Message)
    {
        public OperationGridRow(PurchasingOperationLogEntry entry)
            : this(entry.LoggedAt.ToString("dd.MM.yyyy HH:mm"), entry.Actor, entry.EntityType, entry.EntityNumber, entry.Action, entry.Result, entry.Message)
        {
        }
    }
}


