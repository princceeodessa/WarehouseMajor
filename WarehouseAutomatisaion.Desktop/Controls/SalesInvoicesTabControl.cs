using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class SalesInvoicesTabControl : UserControl
{
    private const string AllStatusesFilter = "Все статусы";

    private readonly SalesWorkspace _workspace;
    private readonly BindingSource _invoicesBindingSource = new();
    private readonly TextBox _searchTextBox = new();
    private readonly ComboBox _statusFilterComboBox = new();
    private readonly DateTimePicker _dateFromPicker = new();
    private readonly DateTimePicker _dateToPicker = new();
    private readonly Label _shownLabel = new();
    private readonly Label _totalInvoicesValueLabel = new();
    private readonly Label _paidInvoicesValueLabel = new();
    private readonly Label _pendingInvoicesValueLabel = new();
    private readonly Label _overdueInvoicesValueLabel = new();
    private readonly DataGridView _grid;
    private readonly System.Windows.Forms.Timer _searchDebounceTimer = new();

    public event Action<SalesWorkspaceNavigationTarget>? NavigateRequested;

    public SalesInvoicesTabControl(SalesWorkspace workspace)
    {
        _workspace = workspace;
        Dock = DockStyle.Fill;
        BackColor = Color.Transparent;

        _grid = DesktopGridFactory.CreateGrid(Array.Empty<InvoiceGridRow>());
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

    public void RefreshView(Guid? selectedInvoiceId = null)
    {
        var currentId = selectedInvoiceId ?? GetSelectedInvoiceId();
        var search = _searchTextBox.Text.Trim();
        var selectedStatus = _statusFilterComboBox.SelectedItem as string ?? AllStatusesFilter;

        var from = _dateFromPicker.Value.Date;
        var to = _dateToPicker.Value.Date;
        if (from > to)
        {
            (from, to) = (to, from);
        }

        var rows = _workspace.Invoices
            .Where(invoice => string.IsNullOrWhiteSpace(search) || MatchesSearch(invoice, search))
            .Where(invoice => string.Equals(selectedStatus, AllStatusesFilter, StringComparison.OrdinalIgnoreCase)
                              || invoice.Status.Equals(selectedStatus, StringComparison.OrdinalIgnoreCase))
            .Where(invoice => invoice.InvoiceDate.Date >= from && invoice.InvoiceDate.Date <= to)
            .OrderByDescending(invoice => invoice.InvoiceDate)
            .ThenByDescending(invoice => invoice.Number)
            .Select(invoice => new InvoiceGridRow(
                invoice.Id,
                invoice.Number,
                invoice.SalesOrderNumber,
                invoice.CustomerName,
                invoice.InvoiceDate.ToString("dd.MM.yyyy"),
                $"{invoice.TotalAmount:N2} ₽",
                BuildPaidAmount(invoice),
                invoice.Status,
                invoice.DueDate.ToString("dd.MM.yyyy"),
                "..."))
            .ToArray();

        _invoicesBindingSource.DataSource = rows;
        ConfigureGridColumns();
        RestoreSelection(currentId);
        RefreshSummaryCards();

        var total = _workspace.Invoices.Count;
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
            Text = "Выставление и контроль оплат по заказам."
        });
        left.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 48,
            Font = DesktopTheme.TitleFont(32f),
            ForeColor = Color.FromArgb(20, 33, 61),
            Text = "Счета"
        });

        _searchTextBox.Width = 320;
        _searchTextBox.Height = 34;
        _searchTextBox.Margin = new Padding(0, 0, 0, 8);
        _searchTextBox.BorderStyle = BorderStyle.FixedSingle;
        _searchTextBox.BackColor = Color.White;
        _searchTextBox.ForeColor = Color.FromArgb(67, 79, 104);
        _searchTextBox.Font = DesktopTheme.BodyFont(10f);
        _searchTextBox.PlaceholderText = "Поиск по счетам...";
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
        actionsRow.Controls.Add(CreateHeaderButton("Новый счет", true, (_, _) => CreateInvoice(), new Size(138, 36)));
        actionsRow.Controls.Add(CreateHeaderButton("Импорт", false, (_, _) => ShowInfo("Импорт счетов подключим на следующем шаге."), new Size(104, 36)));

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

        panel.Controls.Add(CreateMetricCard("Всего счетов", _totalInvoicesValueLabel, "+8%", "к прошлому месяцу", Color.FromArgb(96, 183, 121)));
        panel.Controls.Add(CreateMetricCard("Оплачено", _paidInvoicesValueLabel, "+10%", "к прошлому месяцу", Color.FromArgb(70, 180, 116)));
        panel.Controls.Add(CreateMetricCard("Ожидает оплаты", _pendingInvoicesValueLabel, "+6%", "к прошлому месяцу", Color.FromArgb(237, 173, 74)));
        panel.Controls.Add(CreateMetricCard("Просрочено", _overdueInvoicesValueLabel, "-2%", "к прошлому месяцу", Color.FromArgb(239, 112, 112), dangerTrend: true));
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
        panel.Controls.Add(CreateNavigationButton("Счета", true, null));
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
        header.Controls.Add(CreateSectionHeader("Список счетов", "Контроль выставления, оплаты и печати документов."), 0, 0);
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

        var issueButton = CreateHeaderButton("Выставить", false, (_, _) => IssueSelectedInvoice(), new Size(102, 34));
        issueButton.Margin = new Padding(0, 0, 8, 0);
        var paidButton = CreateHeaderButton("Оплачен", false, (_, _) => MarkSelectedInvoicePaid(), new Size(102, 34));
        paidButton.Margin = new Padding(0, 0, 8, 0);
        var printButton = CreateHeaderButton("Печать", false, (_, _) => PrintSelectedInvoice(), new Size(94, 34));
        printButton.Margin = new Padding(0, 0, 8, 0);
        var editButton = CreateHeaderButton("Изменить", false, (_, _) => EditSelectedInvoice(), new Size(106, 34));
        editButton.Margin = new Padding(0, 0, 0, 0);

        panel.Controls.Add(_statusFilterComboBox);
        panel.Controls.Add(dateRangeHost);
        panel.Controls.Add(filterButton);
        panel.Controls.Add(issueButton);
        panel.Controls.Add(paidButton);
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
        pager.Controls.Add(CreatePagerButton("10", false));
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
        _grid.DataSource = _invoicesBindingSource;
        _grid.DoubleClick += (_, _) => EditSelectedInvoice();
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
                case nameof(InvoiceGridRow.Number):
                    column.Width = 118;
                    break;
                case nameof(InvoiceGridRow.Order):
                    column.Width = 114;
                    break;
                case nameof(InvoiceGridRow.Customer):
                    column.Width = 210;
                    break;
                case nameof(InvoiceGridRow.Date):
                    column.Width = 120;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(InvoiceGridRow.Total):
                    column.Width = 126;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    break;
                case nameof(InvoiceGridRow.Paid):
                    column.Width = 126;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                    break;
                case nameof(InvoiceGridRow.Status):
                    column.Width = 136;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(InvoiceGridRow.DueDate):
                    column.Width = 118;
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
                    break;
                case nameof(InvoiceGridRow.Actions):
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

        var statuses = _workspace.InvoiceStatuses
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

        var maxDate = _workspace.Invoices.Count == 0 ? DateTime.Today : _workspace.Invoices.Max(item => item.InvoiceDate).Date;
        var minDate = _workspace.Invoices.Count == 0 ? maxDate.AddDays(-30) : _workspace.Invoices.Min(item => item.InvoiceDate).Date;
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
        var total = _workspace.Invoices.Count;
        var paid = _workspace.Invoices.Count(invoice => IsPaidStatus(invoice.Status));
        var pending = _workspace.Invoices.Count(invoice => IsPendingStatus(invoice.Status));
        var overdue = _workspace.Invoices.Count(invoice => invoice.DueDate.Date < DateTime.Today && !IsPaidStatus(invoice.Status));

        _totalInvoicesValueLabel.Text = total.ToString();
        _paidInvoicesValueLabel.Text = paid.ToString();
        _pendingInvoicesValueLabel.Text = pending.ToString();
        _overdueInvoicesValueLabel.Text = overdue.ToString();
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
                if (row.DataBoundItem is InvoiceGridRow data && data.InvoiceId == selectedId.Value)
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

    private void CreateInvoice()
    {
        var orderId = GetSelectedInvoice()?.SalesOrderId ?? _workspace.Orders.FirstOrDefault()?.Id;
        if (orderId is null)
        {
            ShowInfo("Нет заказов для формирования нового счета.");
            return;
        }

        var draft = _workspace.CreateInvoiceDraftFromOrder(orderId.Value);
        using var form = new SalesInvoiceEditorForm(_workspace, draft);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultInvoice is null)
        {
            return;
        }

        _workspace.AddInvoice(form.ResultInvoice);
        RefreshView(form.ResultInvoice.Id);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EditSelectedInvoice()
    {
        var invoice = GetSelectedInvoice();
        if (invoice is null)
        {
            return;
        }

        using var form = new SalesInvoiceEditorForm(_workspace, invoice);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultInvoice is null)
        {
            return;
        }

        _workspace.UpdateInvoice(form.ResultInvoice);
        RefreshView(form.ResultInvoice.Id);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void IssueSelectedInvoice()
    {
        var invoice = GetSelectedInvoice();
        if (invoice is null)
        {
            ShowInfo("Сначала выберите счет, который нужно выставить.");
            return;
        }

        HandleWorkflowResult(_workspace.MarkInvoiceIssued(invoice.Id), invoice.Id);
    }

    private void MarkSelectedInvoicePaid()
    {
        var invoice = GetSelectedInvoice();
        if (invoice is null)
        {
            ShowInfo("Сначала выберите счет, по которому нужно отметить оплату.");
            return;
        }

        HandleWorkflowResult(_workspace.MarkInvoicePaid(invoice.Id), invoice.Id);
    }

    private void PrintSelectedInvoice()
    {
        var invoice = GetSelectedInvoice();
        if (invoice is null)
        {
            ShowInfo("Сначала выберите счет, который нужно открыть в печатной форме.");
            return;
        }

        using var form = new DocumentPrintPreviewForm(
            $"Печать счета {invoice.Number}",
            SalesDocumentPrintComposer.BuildInvoiceHtml(invoice));
        DialogTabsHost.ShowDialog(form, FindForm());
    }

    private void HandleWorkflowResult(SalesWorkflowActionResult result, Guid? selectedInvoiceId)
    {
        RefreshView(selectedInvoiceId);
        DocumentsChanged?.Invoke(this, EventArgs.Empty);
        MessageBox.Show(
            FindForm(),
            string.IsNullOrWhiteSpace(result.Detail) ? result.Message : $"{result.Message}{Environment.NewLine}{Environment.NewLine}{result.Detail}",
            "Счета",
            MessageBoxButtons.OK,
            result.Succeeded ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private SalesInvoiceRecord? GetSelectedInvoice()
    {
        if (_grid.CurrentRow?.DataBoundItem is not InvoiceGridRow row)
        {
            return null;
        }

        return _workspace.Invoices.FirstOrDefault(item => item.Id == row.InvoiceId);
    }

    private Guid? GetSelectedInvoiceId()
    {
        return _grid.CurrentRow?.DataBoundItem is InvoiceGridRow row ? row.InvoiceId : null;
    }

    private static bool MatchesSearch(SalesInvoiceRecord invoice, string search)
    {
        return invoice.Number.Contains(search, StringComparison.OrdinalIgnoreCase)
               || invoice.SalesOrderNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
               || invoice.CustomerName.Contains(search, StringComparison.OrdinalIgnoreCase)
               || invoice.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
               || invoice.Manager.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildPaidAmount(SalesInvoiceRecord invoice)
    {
        if (IsPaidStatus(invoice.Status))
        {
            return $"{invoice.TotalAmount:N2} ₽";
        }

        if (TextMojibakeFixer.NormalizeText(invoice.Status).Contains("част", StringComparison.OrdinalIgnoreCase))
        {
            return $"{invoice.TotalAmount * 0.5m:N2} ₽";
        }

        return $"0,00 ₽";
    }

    private static bool IsPaidStatus(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return normalized.Contains("оплач", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("paid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPendingStatus(string status)
    {
        var normalized = TextMojibakeFixer.NormalizeText(status);
        return normalized.Contains("ожида", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("выстав", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("част", StringComparison.OrdinalIgnoreCase);
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
        if (!string.Equals(column.DataPropertyName, nameof(InvoiceGridRow.Status), StringComparison.OrdinalIgnoreCase))
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

        if (normalized.Contains("проср", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("отмен", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("ошиб", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(251, 231, 227);
            style.ForeColor = DesktopTheme.Danger;
            return;
        }

        if (normalized.Contains("ожида", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("выстав", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("част", StringComparison.OrdinalIgnoreCase))
        {
            style.BackColor = Color.FromArgb(252, 242, 224);
            style.ForeColor = Color.FromArgb(180, 116, 22);
            return;
        }

        if (normalized.Contains("чернов", StringComparison.OrdinalIgnoreCase)
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
        MessageBox.Show(FindForm(), message, "Счета", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private sealed record InvoiceGridRow(
        [property: Browsable(false)] Guid InvoiceId,
        [property: DisplayName("№ счета")] string Number,
        [property: DisplayName("Заказ")] string Order,
        [property: DisplayName("Клиент")] string Customer,
        [property: DisplayName("Дата счета")] string Date,
        [property: DisplayName("Сумма")] string Total,
        [property: DisplayName("Оплачено")] string Paid,
        [property: DisplayName("Статус")] string Status,
        [property: DisplayName("Срок оплаты")] string DueDate,
        [property: DisplayName("Действия")] string Actions);
}
