using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class SalesCustomersTabControl : UserControl
{
    private const string AllStatusesFilter = "Все статусы";
    private const string AllTypesFilter = "Тип клиента";

    private readonly SalesWorkspace _workspace;
    private readonly BindingSource _customersBindingSource = new();
    private readonly TextBox _searchTextBox = new();
    private readonly ComboBox _statusFilterComboBox = new();
    private readonly ComboBox _typeFilterComboBox = new();
    private readonly DateTimePicker _dateFromPicker = new();
    private readonly DateTimePicker _dateToPicker = new();
    private readonly Label _shownLabel = new();
    private readonly Label _totalCustomersValueLabel = new();
    private readonly Label _newCustomersValueLabel = new();
    private readonly Label _activeCustomersValueLabel = new();
    private readonly Label _inactiveCustomersValueLabel = new();
    private readonly DataGridView _grid;
    private readonly System.Windows.Forms.Timer _searchDebounceTimer = new();

    public event Action<SalesWorkspaceNavigationTarget>? NavigateRequested;

    public SalesCustomersTabControl(SalesWorkspace workspace)
    {
        _workspace = workspace;
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;

        _grid = DesktopGridFactory.CreateGrid(Array.Empty<CustomerGridRow>());
        _grid.CellFormatting += HandleStatusCellFormatting;

        _searchDebounceTimer.Interval = 180;
        _searchDebounceTimer.Tick += HandleSearchDebounceTick;

        ConfigureStatusFilter();
        ConfigureTypeFilter();
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

    public void RefreshView(Guid? selectedCustomerId = null)
    {
        var currentId = selectedCustomerId ?? GetSelectedCustomerId();
        var search = _searchTextBox.Text.Trim();
        var selectedStatus = _statusFilterComboBox.SelectedItem as string ?? AllStatusesFilter;
        var selectedType = _typeFilterComboBox.SelectedItem as string ?? AllTypesFilter;

        var from = _dateFromPicker.Value.Date;
        var to = _dateToPicker.Value.Date;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var rows = _workspace.Customers
            .Where(customer => string.IsNullOrWhiteSpace(search) || MatchesSearch(customer, search))
            .Where(customer => string.Equals(selectedStatus, AllStatusesFilter, StringComparison.OrdinalIgnoreCase)
                               || customer.Status.Equals(selectedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(customer => string.Equals(selectedType, AllTypesFilter, StringComparison.OrdinalIgnoreCase)
                               || DetermineCustomerType(customer).Equals(selectedType, StringComparison.OrdinalIgnoreCase))
            .Select(customer => new CustomerGridRow(
                customer.Id,
                customer.Name,
                DetermineCustomerType(customer),
                BuildPseudoInn(customer.Code),
                customer.Manager,
                customer.Phone,
                customer.Email,
                customer.Status,
                "..."))
            .OrderBy(row => row.Name)
            .ToArray();

        _customersBindingSource.DataSource = rows;
        ConfigureGridColumns();
        RestoreSelection(currentId);
        RefreshSummaryCards();

        var total = _workspace.Customers.Count;
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
            Text = "Клиентская база и быстрый переход к заказам."
        });
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Font = DesktopTheme.TitleFont(32f),
            ForeColor = Color.FromArgb(20, 33, 61),
            Text = "Клиенты"
        });

        _searchTextBox.Width = 320;
        _searchTextBox.Height = 34;
        _searchTextBox.Margin = new Padding(0, 0, 0, 8);
        _searchTextBox.BorderStyle = BorderStyle.FixedSingle;
        _searchTextBox.BackColor = Color.White;
        _searchTextBox.ForeColor = Color.FromArgb(67, 79, 104);
        _searchTextBox.Font = DesktopTheme.BodyFont(10f);
        _searchTextBox.PlaceholderText = "Поиск по клиентам...";
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
        actionsRow.Controls.Add(CreateHeaderButton("Новый клиент", true, (_, _) => CreateCustomer(), new Size(148, 36)));
        actionsRow.Controls.Add(CreateHeaderButton("Импорт", false, (_, _) => ShowInfo("Импорт клиентов подключим на следующем шаге."), new Size(104, 36)));

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

        panel.Controls.Add(CreateMetricCard("Всего клиентов", _totalCustomersValueLabel, "+5%", "к прошлому месяцу", Color.FromArgb(227, 161, 65)));
        panel.Controls.Add(CreateMetricCard("Новых клиентов", _newCustomersValueLabel, "+16%", "к прошлому месяцу", Color.FromArgb(129, 124, 239)));
        panel.Controls.Add(CreateMetricCard("Активные", _activeCustomersValueLabel, "+9%", "к прошлому месяцу", Color.FromArgb(96, 183, 121)));
        panel.Controls.Add(CreateMetricCard("Неактивные", _inactiveCustomersValueLabel, "-3%", "к прошлому месяцу", Color.FromArgb(239, 112, 112), dangerTrend: true));
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
        panel.Controls.Add(CreateNavigationButton("Клиенты", true, null));
        panel.Controls.Add(CreateNavigationButton("Счета", false, () => NavigateRequested?.Invoke(SalesWorkspaceNavigationTarget.Invoices)));
        panel.Controls.Add(CreateNavigationButton("Отгрузки", false, () => NavigateRequested?.Invoke(SalesWorkspaceNavigationTarget.Shipments)));

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
        header.Controls.Add(CreateSectionHeader("Список клиентов", "Контактная база с текущим статусом работы."), 0, 0);
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

        _typeFilterComboBox.Margin = new Padding(0, 0, 10, 0);
        _typeFilterComboBox.Width = 138;
        _typeFilterComboBox.Height = 34;

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
        filterButton.Margin = new Padding(0, 0, 10, 0);

        var orderButton = CreateHeaderButton("Новый заказ", false, (_, _) => CreateOrderForSelectedCustomer(), new Size(128, 34));
        orderButton.Margin = new Padding(0, 0, 0, 0);

        panel.Controls.Add(_statusFilterComboBox);
        panel.Controls.Add(_typeFilterComboBox);
        panel.Controls.Add(dateRangeHost);
        panel.Controls.Add(filterButton);
        panel.Controls.Add(orderButton);

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
        pager.Controls.Add(CreatePagerButton("6", false));
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
        _grid.DataSource = _customersBindingSource;
        _grid.DoubleClick += (_, _) => EditSelectedCustomer();
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
                case nameof(CustomerGridRow.Name):
                    column.Width = 248;
                    break;
                case nameof(CustomerGridRow.Type):
                    column.Width = 104;
                    break;
                case nameof(CustomerGridRow.TaxId):
                    column.Width = 128;
                    break;
                case nameof(CustomerGridRow.Contact):
                    column.Width = 154;
                    break;
                case nameof(CustomerGridRow.Phone):
                    column.Width = 136;
                    break;
                case nameof(CustomerGridRow.Email):
                    column.Width = 178;
                    break;
                case nameof(CustomerGridRow.Status):
                    column.Width = 118;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(CustomerGridRow.Actions):
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

        var statuses = _workspace.CustomerStatuses
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

    private void ConfigureTypeFilter()
    {
        _typeFilterComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _typeFilterComboBox.FlatStyle = FlatStyle.Flat;
        _typeFilterComboBox.BackColor = Color.White;
        _typeFilterComboBox.ForeColor = Color.FromArgb(52, 64, 91);
        _typeFilterComboBox.Font = DesktopTheme.BodyFont(10f);

        _typeFilterComboBox.Items.Clear();
        _typeFilterComboBox.Items.Add(AllTypesFilter);
        _typeFilterComboBox.Items.Add("Юр. лицо");
        _typeFilterComboBox.Items.Add("ИП");
        _typeFilterComboBox.SelectedItem = AllTypesFilter;
        _typeFilterComboBox.SelectedIndexChanged += (_, _) => RefreshView();
    }

    private void ConfigureDatePickers()
    {
        _dateFromPicker.Format = DateTimePickerFormat.Custom;
        _dateFromPicker.CustomFormat = "dd.MM.yyyy";
        _dateToPicker.Format = DateTimePickerFormat.Custom;
        _dateToPicker.CustomFormat = "dd.MM.yyyy";
        _dateFromPicker.Width = 112;
        _dateToPicker.Width = 112;

        _dateFromPicker.Value = DateTime.Today.AddDays(-30);
        _dateToPicker.Value = DateTime.Today;

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
        var total = _workspace.Customers.Count;
        var active = _workspace.Customers.Count(item =>
            TextMojibakeFixer.NormalizeText(item.Status).Contains("актив", StringComparison.OrdinalIgnoreCase));
        var review = _workspace.Customers.Count(item =>
            TextMojibakeFixer.NormalizeText(item.Status).Contains("провер", StringComparison.OrdinalIgnoreCase));
        var inactive = Math.Max(0, total - active);

        _totalCustomersValueLabel.Text = total.ToString();
        _newCustomersValueLabel.Text = review.ToString();
        _activeCustomersValueLabel.Text = active.ToString();
        _inactiveCustomersValueLabel.Text = inactive.ToString();
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
                if (row.DataBoundItem is CustomerGridRow data && data.CustomerId == selectedId.Value)
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

    private void CreateCustomer()
    {
        var draft = _workspace.CreateCustomerDraft();
        using var form = new CustomerEditorForm(_workspace, draft);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultCustomer is null)
        {
            return;
        }

        _workspace.AddCustomer(form.ResultCustomer);
        RefreshView(form.ResultCustomer.Id);
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
        RefreshView(form.ResultCustomer.Id);
    }

    private void CreateOrderForSelectedCustomer()
    {
        var customer = GetSelectedCustomer();
        if (customer is null)
        {
            ShowInfo("Сначала выберите клиента, для которого нужно создать заказ.");
            return;
        }

        var draft = _workspace.CreateOrderDraft(customer.Id);
        using var form = new SalesOrderEditorForm(_workspace, draft, customer.Id);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultOrder is null)
        {
            return;
        }

        _workspace.AddOrder(form.ResultOrder);
        RefreshView(customer.Id);
    }

    private SalesCustomerRecord? GetSelectedCustomer()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CustomerGridRow row)
        {
            return null;
        }

        return _workspace.Customers.FirstOrDefault(item => item.Id == row.CustomerId);
    }

    private Guid? GetSelectedCustomerId()
    {
        return _grid.CurrentRow?.DataBoundItem is CustomerGridRow row ? row.CustomerId : null;
    }

    private static bool MatchesSearch(SalesCustomerRecord customer, string search)
    {
        return customer.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.ContractNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.Manager.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.Phone.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.Email.Contains(search, StringComparison.OrdinalIgnoreCase)
               || customer.Status.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string DetermineCustomerType(SalesCustomerRecord customer)
    {
        return customer.Name.StartsWith("ИП", StringComparison.OrdinalIgnoreCase) ? "ИП" : "Юр. лицо";
    }

    private static string BuildPseudoInn(string customerCode)
    {
        long hash = 0;
        foreach (var symbol in customerCode)
        {
            hash = (hash * 131 + symbol) % 100_000_000;
        }

        return $"77{hash:00000000}";
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
        if (!string.Equals(column.DataPropertyName, nameof(CustomerGridRow.Status), StringComparison.OrdinalIgnoreCase))
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

        if (normalized.Contains("неактив", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("пауза", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ошиб", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(251, 231, 227);
            style.ForeColor = DesktopTheme.Danger;
            return;
        }

        if (normalized.Contains("провер", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("нов", StringComparison.OrdinalIgnoreCase))
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
        MessageBox.Show(FindForm(), message, "Клиенты", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record CustomerGridRow(
        [property: Browsable(false)] Guid CustomerId,
        [property: DisplayName("Клиент")] string Name,
        [property: DisplayName("Тип")] string Type,
        [property: DisplayName("ИНН")] string TaxId,
        [property: DisplayName("Контактное лицо")] string Contact,
        [property: DisplayName("Телефон")] string Phone,
        [property: DisplayName("E-mail")] string Email,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Действия")] string Actions);
}
