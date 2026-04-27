using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class CatalogWorkspaceControl : UserControl
{
    private readonly SalesWorkspace _salesWorkspace;
    private readonly CatalogWorkspaceStore _store;
    private readonly CatalogWorkspace _workspace;
    private readonly BindingSource _itemsBindingSource = new();
    private readonly BindingSource _priceTypesBindingSource = new();
    private readonly BindingSource _discountsBindingSource = new();
    private readonly BindingSource _documentsBindingSource = new();
    private readonly BindingSource _operationsBindingSource = new();
    private readonly TextBox _itemSearchTextBox = new();
    private readonly Label _defaultPriceTypeLabel = new();
    private readonly Label _itemCountValueLabel = new();
    private readonly Label _priceTypeCountValueLabel = new();
    private readonly Label _discountCountValueLabel = new();
    private readonly Label _documentCountValueLabel = new();
    private readonly Label _itemNameValueLabel = new();
    private readonly Label _itemCodeValueLabel = new();
    private readonly Label _itemCategoryValueLabel = new();
    private readonly Label _itemSupplierValueLabel = new();
    private readonly Label _itemWarehouseValueLabel = new();
    private readonly Label _itemPriceValueLabel = new();
    private readonly Label _itemStatusValueLabel = new();
    private readonly Label _itemBarcodeFormatValueLabel = new();
    private readonly Label _itemBarcodeValueLabel = new();
    private readonly Label _itemQrPayloadValueLabel = new();
    private readonly Label _itemSourceValueLabel = new();
    private readonly Label _itemNotesValueLabel = new();
    private readonly Label _itemsFilteredLabel = new();
    private readonly Label _itemsQualityLabel = new();
    private readonly System.Windows.Forms.Timer _itemSearchDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _persistDebounceTimer = new();
    private readonly System.Windows.Forms.Timer _refreshDebounceTimer = new();
    private readonly Font _itemDetailMonoFont = new("Consolas", 9f, FontStyle.Regular);
    private readonly Font _filterChipFont = new("Segoe UI", 8.8f);
    private readonly Font _filterChipActiveFont = new("Segoe UI Semibold", 8.8f, FontStyle.Bold);
    private readonly Dictionary<CatalogItemFilter, Button> _itemFilterButtons = [];
    private readonly DataGridView _itemsGrid = DesktopGridFactory.CreateGrid(Array.Empty<ItemGridRow>());
    private readonly DataGridView _priceTypesGrid = DesktopGridFactory.CreateGrid(Array.Empty<PriceTypeGridRow>());
    private readonly DataGridView _discountsGrid = DesktopGridFactory.CreateGrid(Array.Empty<DiscountGridRow>());
    private readonly DataGridView _documentsGrid = DesktopGridFactory.CreateGrid(Array.Empty<PriceDocumentGridRow>());
    private readonly DataGridView _operationsGrid = DesktopGridFactory.CreateGrid(Array.Empty<OperationGridRow>());
    private CatalogItemFilter _currentItemFilter = CatalogItemFilter.All;
    private bool _refreshPendingWhileHidden;
    private bool _savePending;
    private bool _catalogSyncPending;
    private bool _notifySalesWorkspacePending;

    public CatalogWorkspaceControl(
        SalesWorkspace salesWorkspace,
        CatalogWorkspaceStore? store = null,
        CatalogWorkspace? workspace = null)
    {
        _salesWorkspace = salesWorkspace;
        _store = store ?? CatalogWorkspaceStore.CreateDefault();
        _workspace = workspace ?? _store.LoadOrCreate(
            string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator) ? Environment.UserName : salesWorkspace.CurrentOperator,
            salesWorkspace);

        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        _itemsGrid.DataSource = _itemsBindingSource;
        _itemsGrid.SelectionChanged += (_, _) => RefreshItemDetails();
        _itemsGrid.DoubleClick += (_, _) => EditSelectedItem();
        _itemsGrid.CellFormatting += HandleItemsGridCellFormatting;
        _priceTypesGrid.DataSource = _priceTypesBindingSource;
        _priceTypesGrid.DoubleClick += (_, _) => EditSelectedPriceType();
        _discountsGrid.DataSource = _discountsBindingSource;
        _discountsGrid.DoubleClick += (_, _) => EditSelectedDiscount();
        _documentsGrid.DataSource = _documentsBindingSource;
        _documentsGrid.DoubleClick += (_, _) => EditSelectedDocument();
        _operationsGrid.DataSource = _operationsBindingSource;
        _itemSearchTextBox.TextChanged += (_, _) => ScheduleItemsRefresh();
        _itemSearchDebounceTimer.Interval = 180;
        _itemSearchDebounceTimer.Tick += HandleItemSearchDebounceTick;
        _persistDebounceTimer.Interval = 750;
        _persistDebounceTimer.Tick += HandlePersistDebounceTick;
        _refreshDebounceTimer.Interval = 120;
        _refreshDebounceTimer.Tick += HandleRefreshDebounceTick;

        BuildLayout();
        ApplyCatalogToSalesWorkspace();
        RefreshAll();

        _workspace.Changed += HandleWorkspaceChanged;
        VisibleChanged += HandleVisibilityChanged;
        Disposed += (_, _) =>
        {
            FlushPendingSave();
            _workspace.Changed -= HandleWorkspaceChanged;
            VisibleChanged -= HandleVisibilityChanged;
            _itemSearchDebounceTimer.Stop();
            _itemSearchDebounceTimer.Tick -= HandleItemSearchDebounceTick;
            _itemSearchDebounceTimer.Dispose();
            _persistDebounceTimer.Stop();
            _persistDebounceTimer.Tick -= HandlePersistDebounceTick;
            _persistDebounceTimer.Dispose();
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Tick -= HandleRefreshDebounceTick;
            _refreshDebounceTimer.Dispose();
            _itemDetailMonoFont.Dispose();
            _filterChipFont.Dispose();
            _filterChipActiveFont.Dispose();
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
        _catalogSyncPending = true;
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

        if (_catalogSyncPending)
        {
            _catalogSyncPending = false;
            ApplyCatalogToSalesWorkspace();
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

    private void ApplyCatalogToSalesWorkspace()
    {
        _salesWorkspace.CatalogItems = _workspace.BuildSalesCatalogItems();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateSummaryNote(), 0, 1);
        root.Controls.Add(CreateSummaryCards(), 0, 2);
        root.Controls.Add(CreateTabs(), 0, 3);

        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Каталог, виды цен и документы установки цен работают в общем desktop-контуре и сохраняются в MySQL.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Номенклатура и цены",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateSummaryNote()
    {
        _defaultPriceTypeLabel.Dock = DockStyle.Top;
        _defaultPriceTypeLabel.Height = 42;
        _defaultPriceTypeLabel.Font = new Font("Segoe UI", 9.2f);
        _defaultPriceTypeLabel.ForeColor = Color.FromArgb(97, 88, 80);

        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52,
            BackColor = Color.FromArgb(255, 250, 241),
            Padding = new Padding(14, 10, 14, 0),
            Margin = new Padding(0, 0, 0, 14)
        };
        panel.Controls.Add(_defaultPriceTypeLabel);
        return panel;
    }

    private Control CreateSummaryCards()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 108,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 12)
        };

        flow.Controls.Add(CreateSummaryCard("Карточек", _itemCountValueLabel, "Текущий каталог товаров и материалов."));
        flow.Controls.Add(CreateSummaryCard("Виды цен", _priceTypeCountValueLabel, "Реальные прайсы из MySQL плюс локальные правила."));
        flow.Controls.Add(CreateSummaryCard("Скидки", _discountCountValueLabel, "Правила маркетинга и спецусловий."));
        flow.Controls.Add(CreateSummaryCard("Документы цен", _documentCountValueLabel, "История автономной установки цен."));

        return flow;
    }

    private Control CreateSummaryCard(string title, Label valueLabel, string hint)
    {
        var panel = new Panel
        {
            Width = 262,
            Height = 92,
            BackColor = Color.White,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(16, 12, 16, 12)
        };

        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 30;
        valueLabel.Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(44, 39, 34);

        panel.Controls.Add(new Label
        {
            Text = hint,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(110, 101, 91)
        });
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 20,
            Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(101, 92, 82)
        });

        return panel;
    }

    private Control CreateTabs()
    {
        var tabs = DesktopSurfaceFactory.CreateTabControl();

        tabs.TabPages.Add(CreateItemsTab());
        tabs.TabPages.Add(CreatePriceTypesTab());
        tabs.TabPages.Add(CreateDiscountsTab());
        tabs.TabPages.Add(CreatePriceDocumentsTab());
        tabs.TabPages.Add(CreateOperationsTab());
        return tabs;
    }

    private TabPage CreateItemsTab()
    {
        var page = new TabPage("Номенклатура") { Padding = new Padding(10) };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));

        root.Controls.Add(CreateItemsListPanel(), 0, 0);
        root.Controls.Add(CreateItemDetailsPanel(), 1, 0);

        page.Controls.Add(root);
        return page;
    }

    private Control CreateItemsListPanel()
    {
        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Padding = new Padding(16);
        panel.Margin = new Padding(0, 0, 12, 0);

        _itemSearchTextBox.PlaceholderText = "Поиск по коду, названию, категории или поставщику";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        header.Controls.Add(new Label
        {
            Text = "Товары, их категории, поставщики и цены доступны без переходов по сложным подсистемам 1С.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Карточки номенклатуры",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        });

        var actions = DesktopSurfaceFactory.CreateToolbarStrip();
        actions.Controls.Add(CreateActionChip("Новая карточка", (_, _) => CreateItem()));
        actions.Controls.Add(CreateActionChip("Изменить карточку", (_, _) => EditSelectedItem()));

        actions.Controls.Add(CreateActionChip("Печать этикетки", (_, _) => PrintSelectedItemLabel()));

        var filters = CreateItemFiltersPanel();
        var digest = CreateItemsDigestPanel();

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_itemSearchTextBox, 0, 1);
        root.Controls.Add(actions, 0, 2);
        root.Controls.Add(filters, 0, 3);
        root.Controls.Add(digest, 0, 4);
        root.Controls.Add(_itemsGrid, 0, 5);

        panel.Controls.Add(root);
        return panel;
    }

    private Control CreateItemDetailsPanel()
    {
        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Padding = new Padding(16);
        panel.Margin = new Padding(0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 13
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        for (var index = 1; index < 12; index++)
        {
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Text = "Карточка",
            Dock = DockStyle.Top,
            Height = 30,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        }, 0, 0);
        root.Controls.Add(CreateDetailRow("Наименование", _itemNameValueLabel), 0, 1);
        root.Controls.Add(CreateDetailRow("Код", _itemCodeValueLabel), 0, 2);
        root.Controls.Add(CreateDetailRow("Категория", _itemCategoryValueLabel), 0, 3);
        root.Controls.Add(CreateDetailRow("Поставщик", _itemSupplierValueLabel), 0, 4);
        root.Controls.Add(CreateDetailRow("Склад", _itemWarehouseValueLabel), 0, 5);
        root.Controls.Add(CreateDetailRow("Цена", _itemPriceValueLabel), 0, 6);
        root.Controls.Add(CreateDetailRow("Статус", _itemStatusValueLabel), 0, 7);
        root.Controls.Add(CreateDetailRow("Источник", _itemSourceValueLabel), 0, 11);
        root.Controls.Add(CreateDetailRow("Barcode format", _itemBarcodeFormatValueLabel), 0, 8);
        root.Controls.Add(CreateDetailRow("Barcode value", _itemBarcodeValueLabel), 0, 9);
        root.Controls.Add(CreateDetailRow("QR payload", _itemQrPayloadValueLabel), 0, 10);
        root.Controls.Add(CreateNotesPanel(), 0, 12);
        _itemBarcodeValueLabel.Font = _itemDetailMonoFont;
        _itemQrPayloadValueLabel.AutoEllipsis = true;

        panel.Controls.Add(root);
        return panel;
    }

    private static Control CreateDetailRow(string caption, Label valueLabel)
    {
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI", 9.8f);
        valueLabel.ForeColor = Color.FromArgb(54, 48, 42);
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            Height = 34,
            Margin = new Padding(0, 0, 0, 6)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.Controls.Add(new Label
        {
            Text = caption,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(103, 94, 84),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        layout.Controls.Add(valueLabel, 1, 0);
        return layout;
    }

    private Control CreateNotesPanel()
    {
        _itemNotesValueLabel.Dock = DockStyle.Fill;
        _itemNotesValueLabel.Font = new Font("Segoe UI", 9.6f);
        _itemNotesValueLabel.ForeColor = Color.FromArgb(54, 48, 42);
        _itemNotesValueLabel.TextAlign = ContentAlignment.TopLeft;
        _itemNotesValueLabel.Padding = new Padding(12);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(255, 250, 241),
            Padding = new Padding(0, 8, 0, 0)
        };
        panel.Controls.Add(_itemNotesValueLabel);
        panel.Controls.Add(new Label
        {
            Text = "Примечания",
            Dock = DockStyle.Top,
            Height = 24,
            Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold),
            ForeColor = Color.FromArgb(103, 94, 84)
        });
        return panel;
    }

    private TabPage CreatePriceTypesTab()
    {
        var page = new TabPage("Виды цен") { Padding = new Padding(10) };
        page.Controls.Add(CreateSimpleGridPanel(
            "Виды цен",
            "Розница, закупка и локальные правила округления.",
            _priceTypesGrid,
            () => CreatePriceType(),
            () => EditSelectedPriceType(),
            null));
        return page;
    }

    private TabPage CreateDiscountsTab()
    {
        var page = new TabPage("Скидки") { Padding = new Padding(10) };
        page.Controls.Add(CreateSimpleGridPanel(
            "Скидки",
            "Маркетинговые и партнерские условия прямо в desktop-контуре.",
            _discountsGrid,
            () => CreateDiscount(),
            () => EditSelectedDiscount(),
            null));
        return page;
    }

    private TabPage CreatePriceDocumentsTab()
    {
        var page = new TabPage("Установка цен") { Padding = new Padding(10) };
        page.Controls.Add(CreateSimpleGridPanel(
            "Документы цен",
            "Локальные документы установки цен, которые формируют автономную историю изменений.",
            _documentsGrid,
            () => CreatePriceDocument(),
            () => EditSelectedDocument(),
            () => ApplySelectedDocument()));
        return page;
    }

    private TabPage CreateOperationsTab()
    {
        var page = new TabPage("Журнал") { Padding = new Padding(10) };
        page.Controls.Add(CreateSimpleGridPanel(
            "Журнал каталога",
            "Последние действия по карточкам, видам цен, скидкам и документам установки цен.",
            _operationsGrid,
            null,
            null,
            null));
        return page;
    }

    private Control CreateSimpleGridPanel(
        string title,
        string hint,
        DataGridView grid,
        Action? createAction,
        Action? editAction,
        Action? tertiaryAction)
    {
        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Padding = new Padding(16);
        panel.Margin = new Padding(0);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        header.Controls.Add(new Label
        {
            Text = hint,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        });

        var actions = DesktopSurfaceFactory.CreateToolbarStrip();
        if (createAction is not null)
        {
            actions.Controls.Add(CreateActionChip("Создать", (_, _) => createAction()));
        }
        if (editAction is not null)
        {
            actions.Controls.Add(CreateActionChip("Изменить", (_, _) => editAction()));
        }
        if (tertiaryAction is not null)
        {
            actions.Controls.Add(CreateActionChip("Провести", (_, _) => tertiaryAction()));
        }

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(grid, 0, 2);
        panel.Controls.Add(root);
        return panel;
    }

    private Button CreateActionChip(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = DesktopTheme.SurfaceMuted,
            ForeColor = DesktopTheme.TextPrimary,
            Font = new Font("Segoe UI Semibold", 9f),
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 8, 8),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = DesktopTheme.BorderStrong;
        button.FlatAppearance.MouseOverBackColor = Color.Empty;
        button.FlatAppearance.MouseDownBackColor = Color.Empty;
        button.MouseEnter += (_, _) => button.BackColor = DesktopTheme.PrimarySoft;
        button.MouseLeave += (_, _) => button.BackColor = DesktopTheme.SurfaceMuted;
        button.Click += handler;
        return button;
    }

    private void ScheduleItemsRefresh()
    {
        _itemSearchDebounceTimer.Stop();
        _itemSearchDebounceTimer.Start();
    }

    private void HandleItemSearchDebounceTick(object? sender, EventArgs e)
    {
        _itemSearchDebounceTimer.Stop();
        RefreshItems();
    }

    private void HandleItemsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0)
        {
            return;
        }

        if (e.ColumnIndex >= _itemsGrid.Columns.Count)
        {
            return;
        }

        var column = _itemsGrid.Columns[e.ColumnIndex];
        if (!string.Equals(column.DataPropertyName, nameof(ItemGridRow.Status), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (e.Value is not string status || string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        var style = e.CellStyle ?? new DataGridViewCellStyle(_itemsGrid.DefaultCellStyle);
        e.CellStyle = style;

        if (status.Contains("архив", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = DesktopTheme.SurfaceMuted;
            style.ForeColor = DesktopTheme.TextMuted;
            return;
        }

        if (status.Contains("актив", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = DesktopTheme.PrimarySoft;
            style.ForeColor = DesktopTheme.SidebarButtonActiveText;
            return;
        }

        style.BackColor = DesktopTheme.InfoSoft;
        style.ForeColor = DesktopTheme.Info;
    }

    private Control CreateItemsDigestPanel()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };

        ConfigureDigestLabel(_itemsFilteredLabel);
        ConfigureDigestLabel(_itemsQualityLabel);

        flow.Controls.Add(_itemsFilteredLabel);
        flow.Controls.Add(_itemsQualityLabel);
        return flow;
    }

    private static void ConfigureDigestLabel(Label label)
    {
        label.AutoSize = true;
        label.Padding = new Padding(10, 4, 10, 4);
        label.Margin = new Padding(0, 0, 8, 0);
        label.Font = DesktopTheme.EmphasisFont(8.8f);
        label.BackColor = DesktopTheme.SurfaceMuted;
        label.ForeColor = DesktopTheme.TextSecondary;
    }

    private Control CreateItemFiltersPanel()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 8)
        };

        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.All, "Все"));
        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.Active, "Активные"));
        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.InSetup, "На настройке"));
        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.Archived, "Архив"));
        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.MissingBarcode, "Без штрихкода"));
        flow.Controls.Add(CreateItemFilterChip(CatalogItemFilter.MissingQr, "Без QR"));
        return flow;
    }

    private Button CreateItemFilterChip(CatalogItemFilter filter, string caption)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Font = _filterChipFont,
            Padding = new Padding(12, 6, 12, 6),
            Margin = new Padding(0, 0, 8, 6),
            Cursor = Cursors.Hand,
            Tag = caption
        };
        button.FlatAppearance.BorderColor = DesktopTheme.Border;
        button.FlatAppearance.MouseOverBackColor = Color.Empty;
        button.FlatAppearance.MouseDownBackColor = Color.Empty;
        button.Click += (_, _) => SetItemFilter(filter);

        _itemFilterButtons[filter] = button;
        return button;
    }

    private void SetItemFilter(CatalogItemFilter filter)
    {
        if (_currentItemFilter == filter)
        {
            return;
        }

        _currentItemFilter = filter;
        _itemSearchDebounceTimer.Stop();
        RefreshItems();
    }

    private void UpdateItemFilterChips()
    {
        foreach (var (filter, button) in _itemFilterButtons)
        {
            var caption = button.Tag as string ?? filter.ToString();
            button.Text = $"{caption}: {CountByFilter(filter):N0}";
            var active = filter == _currentItemFilter;
            button.BackColor = active ? DesktopTheme.PrimarySoft : DesktopTheme.Surface;
            button.ForeColor = active ? DesktopTheme.SidebarButtonActiveText : DesktopTheme.TextSecondary;
            button.FlatAppearance.BorderColor = active ? DesktopTheme.Primary : DesktopTheme.Border;
            button.Font = active ? _filterChipActiveFont : _filterChipFont;
        }
    }

    private int CountByFilter(CatalogItemFilter filter)
    {
        return _workspace.Items.Count(item => MatchesItemFilter(item, filter));
    }

    private void UpdateItemsDigest(IReadOnlyCollection<ItemGridRow> rows)
    {
        var total = _workspace.Items.Count;
        _itemsFilteredLabel.Text = $"Показано: {rows.Count:N0} из {total:N0}";

        if (rows.Count == 0)
        {
            _itemsQualityLabel.Text = "Маркировка: данных нет";
            _itemsQualityLabel.BackColor = DesktopTheme.SurfaceMuted;
            _itemsQualityLabel.ForeColor = DesktopTheme.TextMuted;
            return;
        }

        var visibleIds = rows.Select(item => item.Id).ToHashSet();
        var visibleItems = _workspace.Items.Where(item => visibleIds.Contains(item.Id)).ToArray();
        var missingBarcode = visibleItems.Count(item => string.IsNullOrWhiteSpace(item.BarcodeValue));
        var missingQr = visibleItems.Count(item => string.IsNullOrWhiteSpace(item.QrPayload));
        var hasGaps = missingBarcode + missingQr > 0;

        _itemsQualityLabel.Text = $"Маркировка: без штрихкода {missingBarcode:N0}, без QR {missingQr:N0}";
        _itemsQualityLabel.BackColor = hasGaps ? Color.FromArgb(255, 243, 229) : DesktopTheme.PrimarySoft;
        _itemsQualityLabel.ForeColor = hasGaps ? DesktopTheme.Warning : DesktopTheme.SidebarButtonActiveText;
    }

    private void RefreshAll()
    {
        SuspendLayout();
        try
        {
        _defaultPriceTypeLabel.Text =
            $"Основной вид цены: {_workspace.GetDefaultPriceTypeName()}. Карточки и документы сохраняются в общий MySQL snapshot `catalog`.";

        _itemCountValueLabel.Text = _workspace.Items.Count.ToString("N0");
        _priceTypeCountValueLabel.Text = _workspace.PriceTypes.Count.ToString("N0");
        _discountCountValueLabel.Text = _workspace.Discounts.Count(item => string.Equals(item.Status, "Активна", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        _documentCountValueLabel.Text = _workspace.PriceRegistrations.Count.ToString("N0");

        RefreshItems();
        RefreshPriceTypes();
        RefreshDiscounts();
        RefreshDocuments();
        RefreshOperations();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void RefreshItems()
    {
        var filter = _itemSearchTextBox.Text.Trim();
        var rows = _workspace.Items
            .Where(item => MatchesItem(item, filter))
            .Where(item => MatchesItemFilter(item, _currentItemFilter))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new ItemGridRow(
                item.Id,
                item.Code,
                item.Name,
                item.Category,
                item.Supplier,
                item.DefaultWarehouse,
                item.DefaultPrice,
                item.Status))
            .ToList();
        _itemsBindingSource.DataSource = rows;
        UpdateItemFilterChips();
        UpdateItemsDigest(rows);
        RefreshItemDetails();
    }

    private static bool MatchesItem(CatalogItemRecord item, string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return item.Code.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.Supplier.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || item.DefaultWarehouse.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesItemFilter(CatalogItemRecord item, CatalogItemFilter filter)
    {
        return filter switch
        {
            CatalogItemFilter.Active => item.Status.Contains("актив", StringComparison.OrdinalIgnoreCase),
            CatalogItemFilter.InSetup => item.Status.Contains("настр", StringComparison.OrdinalIgnoreCase) || item.Status.Contains("чернов", StringComparison.OrdinalIgnoreCase),
            CatalogItemFilter.Archived => item.Status.Contains("архив", StringComparison.OrdinalIgnoreCase),
            CatalogItemFilter.MissingBarcode => string.IsNullOrWhiteSpace(item.BarcodeValue),
            CatalogItemFilter.MissingQr => string.IsNullOrWhiteSpace(item.QrPayload),
            _ => true
        };
    }

    private void RefreshItemDetails()
    {
        var selectedId = (_itemsGrid.CurrentRow?.DataBoundItem as ItemGridRow)?.Id;
        var item = selectedId.HasValue
            ? _workspace.Items.FirstOrDefault(candidate => candidate.Id == selectedId.Value)
            : _workspace.Items.FirstOrDefault();

        _itemNameValueLabel.Text = item?.Name ?? "Выберите карточку";
        _itemCodeValueLabel.Text = item?.Code ?? "—";
        _itemCategoryValueLabel.Text = item?.Category ?? "—";
        _itemSupplierValueLabel.Text = item?.Supplier ?? "—";
        _itemWarehouseValueLabel.Text = item?.DefaultWarehouse ?? "—";
        _itemPriceValueLabel.Text = item is null ? "—" : $"{item.DefaultPrice:N2} {item.CurrencyCode}";
        _itemStatusValueLabel.Text = item?.Status ?? "—";
        _itemBarcodeFormatValueLabel.Text = item?.BarcodeFormat ?? "—";
        _itemBarcodeValueLabel.Text = item?.BarcodeValue ?? "—";
        _itemQrPayloadValueLabel.Text = string.IsNullOrWhiteSpace(item?.QrPayload)
            ? "—"
            : item.QrPayload.Length <= 90
                ? item.QrPayload
                : item.QrPayload[..87] + "...";
        _itemSourceValueLabel.Text = item?.SourceLabel ?? "—";
        _itemNotesValueLabel.Text = string.IsNullOrWhiteSpace(item?.Notes) ? "Без дополнительных примечаний." : item.Notes;
    }

    private void RefreshPriceTypes()
    {
        _priceTypesBindingSource.DataSource = _workspace.PriceTypes
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new PriceTypeGridRow(
                item.Id,
                item.Code,
                item.Name,
                item.CurrencyCode,
                item.BasePriceTypeName,
                item.RoundingRule,
                item.IsDefault ? "Да" : "Нет",
                item.Status))
            .ToList();
    }

    private void RefreshDiscounts()
    {
        _discountsBindingSource.DataSource = _workspace.Discounts
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new DiscountGridRow(
                item.Id,
                item.Name,
                item.Percent,
                item.PriceTypeName,
                item.Period,
                item.Scope,
                item.Status))
            .ToList();
    }

    private void RefreshDocuments()
    {
        _documentsBindingSource.DataSource = _workspace.PriceRegistrations
            .OrderByDescending(item => item.DocumentDate)
            .ThenByDescending(item => item.Number, StringComparer.OrdinalIgnoreCase)
            .Select(item => new PriceDocumentGridRow(
                item.Id,
                item.Number,
                item.DocumentDate,
                item.PriceTypeName,
                item.Lines.Count,
                item.Status,
                item.Comment))
            .ToList();
    }

    private void RefreshOperations()
    {
        _operationsBindingSource.DataSource = _workspace.OperationLog
            .OrderByDescending(item => item.LoggedAt)
            .Select(item => new OperationGridRow(
                item.LoggedAt,
                item.Actor,
                item.EntityType,
                item.EntityNumber,
                item.Action,
                item.Result,
                item.Message))
            .ToList();
    }

    private void CreateItem()
    {
        using var dialog = new CatalogItemEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultItem is not null)
        {
            _workspace.UpsertItem(dialog.ResultItem);
        }
    }

    private void EditSelectedItem()
    {
        var row = _itemsGrid.CurrentRow?.DataBoundItem as ItemGridRow;
        if (row is null)
        {
            return;
        }

        var item = _workspace.Items.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (item is null)
        {
            return;
        }

        using var dialog = new CatalogItemEditorForm(_workspace, item);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultItem is not null)
        {
            _workspace.UpsertItem(dialog.ResultItem);
        }
    }

    private void PrintSelectedItemLabel()
    {
        var selectedId = (_itemsGrid.CurrentRow?.DataBoundItem as ItemGridRow)?.Id;
        var item = selectedId.HasValue
            ? _workspace.Items.FirstOrDefault(candidate => candidate.Id == selectedId.Value)
            : _workspace.Items.FirstOrDefault();

        if (item is null)
        {
            MessageBox.Show(this, "Нет выбранной карточки номенклатуры.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new DocumentPrintPreviewForm("Этикетка номенклатуры", CatalogLabelPrintComposer.BuildItemLabelHtml(item));
        DialogTabsHost.ShowDialog(form, this);
    }

    private void CreatePriceType()
    {
        using var dialog = new CatalogPriceTypeEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultPriceType is not null)
        {
            _workspace.UpsertPriceType(dialog.ResultPriceType);
        }
    }

    private void EditSelectedPriceType()
    {
        var row = _priceTypesGrid.CurrentRow?.DataBoundItem as PriceTypeGridRow;
        if (row is null)
        {
            return;
        }

        var priceType = _workspace.PriceTypes.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (priceType is null)
        {
            return;
        }

        using var dialog = new CatalogPriceTypeEditorForm(_workspace, priceType);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultPriceType is not null)
        {
            _workspace.UpsertPriceType(dialog.ResultPriceType);
        }
    }

    private void CreateDiscount()
    {
        using var dialog = new CatalogDiscountEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultDiscount is not null)
        {
            _workspace.UpsertDiscount(dialog.ResultDiscount);
        }
    }

    private void EditSelectedDiscount()
    {
        var row = _discountsGrid.CurrentRow?.DataBoundItem as DiscountGridRow;
        if (row is null)
        {
            return;
        }

        var discount = _workspace.Discounts.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (discount is null)
        {
            return;
        }

        using var dialog = new CatalogDiscountEditorForm(_workspace, discount);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultDiscount is not null)
        {
            _workspace.UpsertDiscount(dialog.ResultDiscount);
        }
    }

    private void CreatePriceDocument()
    {
        using var dialog = new CatalogPriceRegistrationEditorForm(_workspace);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultDocument is not null)
        {
            _workspace.UpsertPriceRegistration(dialog.ResultDocument);
        }
    }

    private void EditSelectedDocument()
    {
        var row = _documentsGrid.CurrentRow?.DataBoundItem as PriceDocumentGridRow;
        if (row is null)
        {
            return;
        }

        var document = _workspace.PriceRegistrations.FirstOrDefault(candidate => candidate.Id == row.Id);
        if (document is null)
        {
            return;
        }

        using var dialog = new CatalogPriceRegistrationEditorForm(_workspace, document);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK && dialog.ResultDocument is not null)
        {
            _workspace.UpsertPriceRegistration(dialog.ResultDocument);
        }
    }

    private void ApplySelectedDocument()
    {
        var row = _documentsGrid.CurrentRow?.DataBoundItem as PriceDocumentGridRow;
        if (row is null)
        {
            return;
        }

        if (_workspace.ApplyPriceRegistration(row.Id, out var message))
        {
            MessageBox.Show(this, message, "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        else
        {
            MessageBox.Show(this, message, "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private enum CatalogItemFilter
    {
        All,
        Active,
        InSetup,
        Archived,
        MissingBarcode,
        MissingQr
    }

    private sealed record ItemGridRow(
        Guid Id,
        string Code,
        string Name,
        string Category,
        string Supplier,
        string DefaultWarehouse,
        decimal DefaultPrice,
        string Status);

    private sealed record PriceTypeGridRow(
        Guid Id,
        string Code,
        string Name,
        string CurrencyCode,
        string BasePriceTypeName,
        string RoundingRule,
        string IsDefault,
        string Status);

    private sealed record DiscountGridRow(
        Guid Id,
        string Name,
        decimal Percent,
        string PriceTypeName,
        string Period,
        string Scope,
        string Status);

    private sealed record PriceDocumentGridRow(
        Guid Id,
        string Number,
        DateTime DocumentDate,
        string PriceTypeName,
        int LinesCount,
        string Status,
        string Comment);

    private sealed record OperationGridRow(
        DateTime LoggedAt,
        string Actor,
        string EntityType,
        string EntityNumber,
        string Action,
        string Result,
        string Message);
}
