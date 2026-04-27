using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class SalesShipmentsTabControl : UserControl
{
    private const string AllStatusesFilter = "Все статусы";

    private readonly SalesWorkspace _workspace;
    private readonly BindingSource _shipmentsBindingSource = new();
    private readonly TextBox _searchTextBox = new();
    private readonly ComboBox _statusFilterComboBox = new();
    private readonly DateTimePicker _dateFromPicker = new();
    private readonly DateTimePicker _dateToPicker = new();
    private readonly Label _shownLabel = new();
    private readonly Label _totalShipmentsValueLabel = new();
    private readonly Label _deliveredShipmentsValueLabel = new();
    private readonly Label _inTransitShipmentsValueLabel = new();
    private readonly Label _issueShipmentsValueLabel = new();
    private readonly DataGridView _grid;
    private readonly System.Windows.Forms.Timer _searchDebounceTimer = new();

    public event Action<SalesWorkspaceNavigationTarget>? NavigateRequested;

    public SalesShipmentsTabControl(SalesWorkspace workspace)
    {
        _workspace = workspace;
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;

        _grid = DesktopGridFactory.CreateGrid(Array.Empty<ShipmentGridRow>());
        _grid.CellFormatting += HandleStatusCellFormatting;

        _searchDebounceTimer.Interval = 180;
        _searchDebounceTimer.Tick += HandleSearchDebounceTick;

        ConfigureStatusFilter();
        ConfigureDatePickers();
        BuildLayout();
        WireBindings();
        RefreshView();

        Disposed += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Tick -= HandleSearchDebounceTick;
            _searchDebounceTimer.Dispose();
            _grid.CellFormatting -= HandleStatusCellFormatting;
        };
    }

    public event EventHandler? DocumentsChanged;

    public void RefreshView(Guid? selectedShipmentId = null)
    {
        var currentId = selectedShipmentId ?? GetSelectedShipmentId();
        var search = _searchTextBox.Text.Trim();
        var selectedStatus = _statusFilterComboBox.SelectedItem as string ?? AllStatusesFilter;

        var from = _dateFromPicker.Value.Date;
        var to = _dateToPicker.Value.Date;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var rows = _workspace.Shipments
            .Where(shipment => string.IsNullOrWhiteSpace(search) || MatchesSearch(shipment, search))
            .Where(shipment => string.Equals(selectedStatus, AllStatusesFilter, StringComparison.OrdinalIgnoreCase)
                               || shipment.Status.Equals(selectedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(shipment => shipment.ShipmentDate.Date >= from && shipment.ShipmentDate.Date <= to)
            .OrderByDescending(shipment => shipment.ShipmentDate)
            .ThenByDescending(shipment => shipment.Number)
            .Select(shipment => new ShipmentGridRow(
                shipment.Id,
                shipment.Number,
                shipment.SalesOrderNumber,
                shipment.CustomerName,
                shipment.ShipmentDate.ToString("dd.MM.yyyy"),
                shipment.Carrier,
                shipment.Status,
                shipment.ShipmentDate.AddDays(2).ToString("dd.MM.yyyy"),
                "..."))
            .ToArray();

        _shipmentsBindingSource.DataSource = rows;
        ConfigureGridColumns();
        RestoreSelection(currentId);
        RefreshSummaryCards();

        var total = _workspace.Shipments.Count;
        var shownFrom = rows.Length == 0 ? 0 : 1;
        _shownLabel.Text = $"Показано {shownFrom:N0}–{rows.Length:N0} из {total:N0}";
    }

    private void BuildLayout()
    {
        var canvas = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = DesktopTheme.AppBackground,
            Padding = new Padding(16, 14, 16, 16)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Margin = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 4));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateMetricsStrip(), 0, 1);
        root.Controls.Add(CreateNavigationStrip(), 0, 2);
        root.Controls.Add(CreateMainListShell(), 0, 3);
        root.Controls.Add(new Panel { Height = 4, Dock = DockStyle.Top, Margin = new Padding(0) }, 0, 4);

        canvas.Controls.Add(root);
        Controls.Add(canvas);
    }

    private Control CreateHeader()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        var left = new Panel
        {
            Dock = DockStyle.Fill,
            Height = 78,
            Margin = new Padding(0)
        };
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            Font = DesktopTheme.BodyFont(10.2f),
            ForeColor = Color.FromArgb(106, 118, 142),
            Text = "Контроль сборки, логистики и статусов доставки."
        });
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Font = DesktopTheme.TitleFont(32f),
            ForeColor = Color.FromArgb(20, 33, 61),
            Text = "Отгрузки"
        });

        _searchTextBox.Width = 320;
        _searchTextBox.Height = 34;
        _searchTextBox.Margin = new Padding(0, 0, 0, 8);
        _searchTextBox.BorderStyle = BorderStyle.FixedSingle;
        _searchTextBox.BackColor = Color.White;
        _searchTextBox.ForeColor = Color.FromArgb(67, 79, 104);
        _searchTextBox.Font = DesktopTheme.BodyFont(10f);
        _searchTextBox.PlaceholderText = "Поиск по отгрузкам...";
        _searchTextBox.TextChanged += (_, _) => ScheduleSearchRefresh();

        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var searchRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        searchRow.Controls.Add(_searchTextBox);

        var actionsRow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0)
        };
        actionsRow.Controls.Add(CreateHeaderButton("Новая отгрузка", true, (_, _) => CreateShipment(), new Size(160, 36)));
        actionsRow.Controls.Add(CreateHeaderButton("Импорт", false, (_, _) => ShowInfo("Импорт отгрузок подключим на следующем шаге."), new Size(104, 36)));

        right.Controls.Add(searchRow, 0, 0);
        right.Controls.Add(actionsRow, 0, 1);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(right, 1, 0);
        return root;
    }

    private Control CreateMetricsStrip()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 12),
            Margin = new Padding(0)
        };

        panel.Controls.Add(CreateMetricCard("Всего отгрузок", _totalShipmentsValueLabel, "+15%", "к прошлому месяцу", Color.FromArgb(96, 183, 121)));
        panel.Controls.Add(CreateMetricCard("Доставлено", _deliveredShipmentsValueLabel, "+12%", "к прошлому месяцу", Color.FromArgb(88, 131, 224)));
        panel.Controls.Add(CreateMetricCard("В пути", _inTransitShipmentsValueLabel, "+9%", "к прошлому месяцу", Color.FromArgb(119, 169, 105)));
        panel.Controls.Add(CreateMetricCard("Проблемы", _issueShipmentsValueLabel, "-1%", "к прошлому месяцу", Color.FromArgb(239, 112, 112), dangerTrend: true));
        return panel;
    }

    private Control CreateNavigationStrip()
    {
        var container = new Panel
        {
            Dock = DockStyle.Top,
            Height = 42,
            Margin = new Padding(0, 0, 0, 10),
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

        panel.Controls.Add(CreateNavigationButton("Заказы", false, () => NavigateRequested?.Invoke(SalesWorkspaceNavigationTarget.Orders)));
        panel.Controls.Add(CreateNavigationButton("Клиенты", false, () => NavigateRequested?.Invoke(SalesWorkspaceNavigationTarget.Customers)));
        panel.Controls.Add(CreateNavigationButton("Счета", false, () => NavigateRequested?.Invoke(SalesWorkspaceNavigationTarget.Invoices)));
        panel.Controls.Add(CreateNavigationButton("Отгрузки", true, null));

        container.Controls.Add(panel);
        return container;
    }

    private Control CreateMainListShell()
    {
        var shell = DesktopSurfaceFactory.CreateCardShell();
        shell.Dock = DockStyle.Top;
        shell.AutoSize = true;
        shell.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        shell.Padding = new Padding(18, 14, 18, 12);
        shell.Margin = new Padding(0, 0, 0, 10);
        shell.BackColor = Color.White;

        _grid.Height = 520;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 54,
            Margin = new Padding(0, 0, 0, 6)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));
        header.Controls.Add(CreateSectionHeader("Список отгрузок", "Оперативный контроль логистики и выполнения заказов."), 0, 0);
        header.Controls.Add(new Label
        {
            Text = "⚙",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Symbol", 11f, FontStyle.Regular),
            ForeColor = Color.FromArgb(107, 119, 146),
            TextAlign = ContentAlignment.MiddleCenter
        }, 1, 0);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(CreateFiltersRow(), 0, 1);
        root.Controls.Add(_grid, 0, 2);
        root.Controls.Add(CreateFooter(), 0, 3);

        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateFiltersRow()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 10),
            Margin = new Padding(0)
        };

        _statusFilterComboBox.Margin = new Padding(0, 0, 10, 0);
        _statusFilterComboBox.Width = 156;
        _statusFilterComboBox.Height = 34;

        _dateFromPicker.Margin = new Padding(4, 4, 2, 0);
        _dateToPicker.Margin = new Padding(2, 4, 4, 0);

        var dateRangeHost = new Panel
        {
            Width = 250,
            Height = 36,
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
        dateRangeLayout.Controls.Add(_dateFromPicker, 0, 0);
        dateRangeLayout.Controls.Add(new Label
        {
            Text = "—",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.FromArgb(126, 137, 161),
            Font = DesktopTheme.BodyFont(9f)
        }, 1, 0);
        dateRangeLayout.Controls.Add(_dateToPicker, 2, 0);
        dateRangeHost.Controls.Add(dateRangeLayout);

        var filterButton = CreateHeaderButton("Фильтры", false, (_, _) => ShowInfo("Расширенные фильтры подключим на следующем шаге."), new Size(108, 34));
        filterButton.Margin = new Padding(0, 0, 8, 0);

        var prepareButton = CreateHeaderButton("К сборке", false, (_, _) => PrepareSelectedShipment(), new Size(102, 34));
        prepareButton.Margin = new Padding(0, 0, 8, 0);
        var shipButton = CreateHeaderButton("Отгрузить", false, (_, _) => ShipSelectedShipment(), new Size(102, 34));
        shipButton.Margin = new Padding(0, 0, 8, 0);
        var transferButton = CreateHeaderButton("Перемещение", false, (_, _) => CreateTransferForSelectedShipment(), new Size(122, 34));
        transferButton.Margin = new Padding(0, 0, 8, 0);
        var printButton = CreateHeaderButton("Печать", false, (_, _) => PrintSelectedShipment(), new Size(94, 34));
        printButton.Margin = new Padding(0, 0, 8, 0);
        var editButton = CreateHeaderButton("Изменить", false, (_, _) => EditSelectedShipment(), new Size(106, 34));
        editButton.Margin = new Padding(0, 0, 0, 0);

        panel.Controls.Add(_statusFilterComboBox);
        panel.Controls.Add(dateRangeHost);
        panel.Controls.Add(filterButton);
        panel.Controls.Add(prepareButton);
        panel.Controls.Add(shipButton);
        panel.Controls.Add(transferButton);
        panel.Controls.Add(printButton);
        panel.Controls.Add(editButton);
        return panel;
    }

    private Control CreateFooter()
    {
        _shownLabel.AutoSize = true;
        _shownLabel.Margin = new Padding(0, 9, 0, 0);
        _shownLabel.Font = DesktopTheme.BodyFont(9.8f);
        _shownLabel.ForeColor = Color.FromArgb(108, 120, 141);
        _shownLabel.BackColor = Color.Transparent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            Height = 42,
            Margin = new Padding(0, 10, 0, 0)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));

        root.Controls.Add(_shownLabel, 0, 0);

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
        pager.Controls.Add(CreatePagerButton("8", false));
        pager.Controls.Add(CreatePagerButton("...", false));
        pager.Controls.Add(CreatePagerButton("3", false));
        pager.Controls.Add(CreatePagerButton("2", false));
        pager.Controls.Add(CreatePagerButton("1", true));
        pager.Controls.Add(CreatePagerButton("?", false));
        root.Controls.Add(pager, 1, 0);
        return root;
    }

    private void WireBindings()
    {
        _grid.DataSource = _shipmentsBindingSource;
        _grid.DoubleClick += (_, _) => EditSelectedShipment();
    }

    private void ConfigureGridColumns()
    {
        if (_grid.Columns.Count == 0)
        {
            return;
        }

        _grid.RowTemplate.Height = 38;
        _grid.ColumnHeadersHeight = 36;
        _grid.ColumnHeadersDefaultCellStyle.Font = DesktopTheme.EmphasisFont(9.4f);
        _grid.DefaultCellStyle.Font = DesktopTheme.BodyFont(10f);
        _grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(238, 243, 255);
        _grid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(37, 50, 84);
        _grid.GridColor = Color.FromArgb(233, 238, 248);
        _grid.BackgroundColor = Color.White;
        _grid.AlternatingRowsDefaultCellStyle.BackColor = Color.White;

        foreach (DataGridViewColumn column in _grid.Columns)
        {
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.MinimumWidth = 80;

            switch (column.DataPropertyName)
            {
                case nameof(ShipmentGridRow.Number):
                    column.Width = 122;
                    break;
                case nameof(ShipmentGridRow.Order):
                    column.Width = 112;
                    break;
                case nameof(ShipmentGridRow.Customer):
                    column.Width = 198;
                    break;
                case nameof(ShipmentGridRow.Date):
                    column.Width = 120;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(ShipmentGridRow.Carrier):
                    column.Width = 136;
                    break;
                case nameof(ShipmentGridRow.Status):
                    column.Width = 134;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(ShipmentGridRow.PlannedDate):
                    column.Width = 126;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(ShipmentGridRow.Actions):
                    column.Width = 90;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
            }
        }
    }

    private void ConfigureStatusFilter()
    {
        _statusFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusFilterComboBox.FlatStyle = FlatStyle.Flat;
        _statusFilterComboBox.BackColor = Color.White;
        _statusFilterComboBox.ForeColor = Color.FromArgb(52, 64, 91);
        _statusFilterComboBox.Font = DesktopTheme.BodyFont(10f);

        var statuses = _workspace.ShipmentStatuses
            .Where(status => !string.IsNullOrWhiteSpace(status))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(status => status, StringComparer.OrdinalIgnoreCase)
            .ToList();

        statuses.Insert(0, AllStatusesFilter);

        _statusFilterComboBox.Items.Clear();
        foreach (var status in statuses)
        {
            _statusFilterComboBox.Items.Add(status);
        }

        _statusFilterComboBox.SelectedItem = AllStatusesFilter;
        _statusFilterComboBox.SelectedIndexChanged += (_, _) => RefreshView();
    }

    private void ConfigureDatePickers()
    {
        _dateFromPicker.Format = DateTimePickerFormat.Custom;
        _dateFromPicker.CustomFormat = "dd.MM.yyyy";
        _dateToPicker.Format = DateTimePickerFormat.Custom;
        _dateToPicker.CustomFormat = "dd.MM.yyyy";
        _dateFromPicker.Width = 112;
        _dateToPicker.Width = 112;

        var maxDate = _workspace.Shipments.Count == 0 ? DateTime.Today : _workspace.Shipments.Max(item => item.ShipmentDate).Date;
        var minDate = _workspace.Shipments.Count == 0 ? maxDate.AddDays(-30) : _workspace.Shipments.Min(item => item.ShipmentDate).Date;
        if ((maxDate - minDate).TotalDays > 45)
        {
            minDate = maxDate.AddDays(-30);
        }

        _dateFromPicker.Value = minDate;
        _dateToPicker.Value = maxDate;

        _dateFromPicker.ValueChanged += (_, _) => RefreshView();
        _dateToPicker.ValueChanged += (_, _) => RefreshView();
    }

    private void ScheduleSearchRefresh()
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    private void HandleSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer.Stop();
        RefreshView();
    }

    private void RefreshSummaryCards()
    {
        var total = _workspace.Shipments.Count;
        var delivered = _workspace.Shipments.Count(shipment => IsDeliveredStatus(shipment.Status));
        var inTransit = _workspace.Shipments.Count(shipment => IsInTransitStatus(shipment.Status));
        var issues = _workspace.Shipments.Count(shipment => IsIssueStatus(shipment.Status));

        _totalShipmentsValueLabel.Text = total.ToString();
        _deliveredShipmentsValueLabel.Text = delivered.ToString();
        _inTransitShipmentsValueLabel.Text = inTransit.ToString();
        _issueShipmentsValueLabel.Text = issues.ToString();
    }

    private void RestoreSelection(Guid? selectedId)
    {
        if (_grid.Rows.Count == 0)
        {
            return;
        }

        if (selectedId is not null)
        {
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.DataBoundItem is ShipmentGridRow data && data.ShipmentId == selectedId.Value)
                {
                    row.Selected = true;
                    _grid.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        _grid.Rows[0].Selected = true;
        _grid.CurrentCell = _grid.Rows[0].Cells[0];
    }

    private void CreateShipment()
    {
        var orderId = GetSelectedShipment()?.SalesOrderId ?? _workspace.Orders.FirstOrDefault()?.Id;
        if (orderId is null)
        {
            ShowInfo("Нет заказов для формирования новой отгрузки.");
            return;
        }

        var draft = _workspace.CreateShipmentDraftFromOrder(orderId.Value);
        using var form = new SalesShipmentEditorForm(_workspace, draft);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultShipment is null)
        {
            return;
        }

        _workspace.AddShipment(form.ResultShipment);
        RefreshView(form.ResultShipment.Id);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EditSelectedShipment()
    {
        var shipment = GetSelectedShipment();
        if (shipment is null)
        {
            return;
        }

        using var form = new SalesShipmentEditorForm(_workspace, shipment);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultShipment is null)
        {
            return;
        }

        _workspace.UpdateShipment(form.ResultShipment);
        RefreshView(form.ResultShipment.Id);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PrepareSelectedShipment()
    {
        var shipment = GetSelectedShipment();
        if (shipment is null)
        {
            ShowInfo("Сначала выберите отгрузку, которую нужно передать в сборку.");
            return;
        }

        HandleWorkflowResult(_workspace.PrepareShipment(shipment.Id), shipment.Id);
    }

    private void ShipSelectedShipment()
    {
        var shipment = GetSelectedShipment();
        if (shipment is null)
        {
            ShowInfo("Сначала выберите отгрузку, которую нужно провести.");
            return;
        }

        HandleWorkflowResult(_workspace.ShipShipment(shipment.Id), shipment.Id);
    }

    private void CreateTransferForSelectedShipment()
    {
        var shipment = GetSelectedShipment();
        if (shipment is null)
        {
            ShowInfo("Сначала выберите отгрузку, для которой нужно создать перемещение.");
            return;
        }

        var inventory = new SalesInventoryService(_workspace);
        var check = inventory.AnalyzeShipment(shipment);
        if (check.IsFullyCovered)
        {
            ShowInfo($"Отгрузка {shipment.Number} уже полностью обеспечена на складе {shipment.Warehouse}.");
            return;
        }

        var store = WarehouseOperationalWorkspaceStore.CreateDefault();
        var currentOperator = string.IsNullOrWhiteSpace(_workspace.CurrentOperator) ? Environment.UserName : _workspace.CurrentOperator;
        var warehouseWorkspace = store.LoadOrCreate(currentOperator, _workspace);
        warehouseWorkspace.RefreshReferenceData(_workspace);

        var createdTransfers = BuildTransfersForShipment(shipment, check, warehouseWorkspace);
        if (createdTransfers.Count == 0)
        {
            ShowInfo("Не удалось подобрать склад-источник для дефицитных позиций. Проверьте закупку или создайте перемещение вручную.");
            return;
        }

        foreach (var transfer in createdTransfers)
        {
            warehouseWorkspace.AddTransferOrder(transfer);
        }

        store.Save(warehouseWorkspace);
        _workspace.NotifyExternalChange();
        RefreshView(shipment.Id);

        var unresolvedCount = CountUnresolvedShortages(check, createdTransfers);
        var detail = unresolvedCount == 0
            ? $"Создано перемещений: {createdTransfers.Count}. Они уже доступны в модуле склада."
            : $"Создано перемещений: {createdTransfers.Count}. Остались непокрытые позиции: {unresolvedCount}.";
        MessageBox.Show(FindForm(), detail, $"Перемещение под отгрузку {shipment.Number}", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void PrintSelectedShipment()
    {
        var shipment = GetSelectedShipment();
        if (shipment is null)
        {
            ShowInfo("Сначала выберите отгрузку, которую нужно открыть в печатной форме.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать накладной {shipment.Number}",
            SalesDocumentPrintComposer.BuildShipmentHtml(shipment));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void HandleWorkflowResult(SalesWorkflowActionResult result, Guid? selectedShipmentId)
    {
        RefreshView(selectedShipmentId);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        MessageBox.Show(
            FindForm(),
            string.IsNullOrWhiteSpace(result.Detail) ? result.Message : $"{result.Message}{Environment.NewLine}{Environment.NewLine}{result.Detail}",
            "Отгрузки",
            MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private List<OperationalWarehouseDocumentRecord> BuildTransfersForShipment(
        SalesShipmentRecord shipment,
        SalesInventoryCheck check,
        OperationalWarehouseWorkspace warehouseWorkspace)
    {
        var groupedLines = new Dictionary<string, List<OperationalWarehouseLineRecord>>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in check.Lines.Where(item => item.ShortageQuantity > 0))
        {
            var remaining = line.ShortageQuantity;
            foreach (var alternative in line.Alternatives
                         .Where(item => item.AvailableQuantity > 0
                                        && !item.Warehouse.Equals(shipment.Warehouse, StringComparison.OrdinalIgnoreCase))
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
                    RelatedDocument = shipment.Number
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
            draft.TargetWarehouse = shipment.Warehouse;
            draft.Status = warehouseWorkspace.TransferStatuses.Count > 1
                ? warehouseWorkspace.TransferStatuses[1]
                : draft.Status;
            draft.RelatedDocument = shipment.Number;
            draft.Comment = $"Перемещение под отгрузку {shipment.Number} / заказ {shipment.SalesOrderNumber}";
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

    private SalesShipmentRecord? GetSelectedShipment()
    {
        if (_grid.CurrentRow?.DataBoundItem is not ShipmentGridRow row)
        {
            return null;
        }

        return _workspace.Shipments.FirstOrDefault(item => item.Id == row.ShipmentId);
    }

    private Guid? GetSelectedShipmentId()
    {
        return _grid.CurrentRow?.DataBoundItem is ShipmentGridRow row ? row.ShipmentId : null;
    }

    private static bool MatchesSearch(SalesShipmentRecord shipment, string search)
    {
        return shipment.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
               || shipment.SalesOrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
               || shipment.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || shipment.Warehouse.Contains(search, StringComparison.OrdinalIgnoreCase)
               || shipment.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
               || shipment.Carrier.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDeliveredStatus(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return normalized.Contains("отгруж", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("достав", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("заверш", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInTransitStatus(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return normalized.Contains("в пути", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("сбор", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("готов", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("резерв", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsIssueStatus(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return normalized.Contains("ошиб", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("отмен", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("задерж", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("проблем", StringComparison.OrdinalIgnoreCase);
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
        if (!string.Equals(column.DataPropertyName, nameof(ShipmentGridRow.Status), StringComparison.OrdinalIgnoreCase))
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
            || normalized.Contains("отмен", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("задерж", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("проблем", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(251, 231, 227);
            style.ForeColor = DesktopTheme.Danger;
            return;
        }

        if (normalized.Contains("в пути", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("сбор", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("готов", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(235, 240, 255);
            style.ForeColor = Color.FromArgb(69, 90, 186);
            return;
        }

        style.BackColor = Color.FromArgb(229, 244, 234);
        style.ForeColor = Color.FromArgb(49, 146, 87);
    }

    private static Control CreateNavigationButton(string text, bool active, Action? handler)
    {
        var host = new Panel
        {
            Width = 116,
            Height = 40,
            Margin = new Padding(0)
        };

        var button = new Button
        {
            Dock = DockStyle.Fill,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            Font = active ? DesktopTheme.EmphasisFont(10f) : DesktopTheme.BodyFont(10f),
            BackColor = Color.Transparent,
            ForeColor = active ? Color.FromArgb(84, 97, 245) : Color.FromArgb(84, 96, 122),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        button.FlatAppearance.BorderSize = 0;
        if (handler is not null)
        {
            button.Click += (_, _) => handler();
        }

        host.Controls.Add(button);
        if (active)
        {
            host.Controls.Add(new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Color.FromArgb(84, 97, 245)
            });
        }

        return host;
    }

    private static Button CreateHeaderButton(string text, bool primary, EventHandler handler, Size size)
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

    private static Control CreateMetricCard(string title, Label valueLabel, string trend, string trendHint, Color accent, bool dangerTrend = false)
    {
        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 40;
        valueLabel.Font = DesktopTheme.TitleFont(26f);
        valueLabel.ForeColor = Color.FromArgb(20, 33, 61);
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var card = DesktopSurfaceFactory.CreateCardShell();
        card.Dock = DockStyle.None;
        card.Width = 220;
        card.Height = 152;
        card.Margin = new Padding(0, 0, 14, 10);
        card.Padding = new Padding(14, 14, 14, 10);
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
            Margin = new Padding(0, 0, 0, 8)
        };
        caption.Controls.Add(new RoundedSurfacePanel
        {
            Width = 36,
            Height = 36,
            BackColor = Color.FromArgb(44, accent),
            BorderColor = Color.FromArgb(65, accent),
            BorderThickness = 0,
            CornerRadius = 10,
            DrawShadow = false,
            Margin = new Padding(0, 0, 8, 0)
        });
        caption.Controls.Add(new Label
        {
            AutoSize = true,
            Font = DesktopTheme.EmphasisFont(10.2f),
            ForeColor = Color.FromArgb(51, 65, 95),
            Text = title,
            Margin = new Padding(0, 8, 0, 0)
        });

        root.Controls.Add(caption, 0, 0);
        root.Controls.Add(valueLabel, 0, 1);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = DesktopTheme.EmphasisFont(10f),
            ForeColor = dangerTrend ? DesktopTheme.Danger : Color.FromArgb(48, 166, 99),
            Text = trend,
            Margin = new Padding(0)
        }, 0, 2);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Font = DesktopTheme.BodyFont(9.2f),
            ForeColor = Color.FromArgb(112, 124, 146),
            Text = trendHint,
            Margin = new Padding(0)
        }, 0, 3);

        card.Controls.Add(root);
        return card;
    }

    private static Control CreateSectionHeader(string title, string subtitle)
    {
        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52
        };

        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 22,
            Font = DesktopTheme.BodyFont(9f),
            ForeColor = DesktopTheme.TextSecondary
        });

        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = DesktopTheme.TitleFont(12f),
            ForeColor = Color.FromArgb(20, 33, 61)
        });

        return header;
    }

    private static Control CreatePagerButton(string text, bool active)
    {
        return new Label
        {
            AutoSize = false,
            Width = 30,
            Height = 30,
            Text = text,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = active ? DesktopTheme.EmphasisFont(9.4f) : DesktopTheme.BodyFont(9.4f),
            ForeColor = active ? Color.FromArgb(84, 97, 245) : Color.FromArgb(100, 113, 140),
            BackColor = active ? Color.FromArgb(240, 242, 255) : Color.FromArgb(250, 251, 254),
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(6, 0, 0, 0)
        };
    }

    private void ShowInfo(string message)
    {
        MessageBox.Show(FindForm(), message, "Отгрузки", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record ShipmentGridRow(
        [property: Browsable(false)] Guid ShipmentId,
        [property: DisplayName("№ отгрузки")] string Number,
        [property: DisplayName("Заказ")] string Order,
        [property: DisplayName("Клиент")] string Customer,
        [property: DisplayName("Дата отгрузки")] string Date,
        [property: DisplayName("Способ доставки")] string Carrier,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("План. доставка")] string PlannedDate,
        [property: DisplayName("Действия")] string Actions);
}
