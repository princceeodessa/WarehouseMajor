using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class SalesOrderEditorForm : Form
{
    private readonly SalesWorkspace _workspace;
    private readonly SalesInventoryService _inventory;
    private readonly SalesOrderRecord _draft;
    private readonly BindingList<SalesOrderLineRecord> _lines;
    private readonly BindingSource _linesBindingSource = new();
    private readonly TextBox _numberTextBox = new();
    private readonly DateTimePicker _datePicker = new();
    private readonly ComboBox _customerComboBox = new();
    private readonly ComboBox _warehouseComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly ComboBox _managerComboBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly Label _contractValueLabel = new();
    private readonly Label _currencyValueLabel = new();
    private readonly Label _totalValueLabel = new();
    private readonly Label _stockStatusValueLabel = new();
    private readonly Label _stockHintValueLabel = new();
    private readonly DataGridView _linesGrid = new();

    public SalesOrderEditorForm(
        SalesWorkspace workspace,
        SalesOrderRecord? order = null,
        Guid? preselectedCustomerId = null)
    {
        _workspace = workspace;
        _inventory = new SalesInventoryService(workspace);
        _draft = order?.Clone() ?? workspace.CreateOrderDraft(preselectedCustomerId);
        _lines = new BindingList<SalesOrderLineRecord>(_draft.Lines.Select(line => line.Clone()).ToList());
        _lines.ListChanged += (_, _) =>
        {
            RefreshTotals();
            RefreshStockStatus();
        };
        ResultOrder = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = order is null ? "Заказ покупателя" : $"Заказ покупателя {order.Number}";

        BuildLayout();
        LoadDraft();
    }

    public SalesOrderRecord? ResultOrder { get; private set; }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateHeaderFields(), 0, 1);
        root.Controls.Add(CreateLinesSection(), 0, 2);
        root.Controls.Add(CreateButtons(), 0, 3);
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
            Text = "Шапка заказа, клиент, склад, статус и табличная часть по товарам.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });

        panel.Controls.Add(new Label
        {
            Text = "Заказ покупателя",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        return panel;
    }

    private Control CreateHeaderFields()
    {
        _customerComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _customerComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _customerComboBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _customerComboBox.DisplayMember = nameof(CustomerOption.DisplayName);
        _customerComboBox.ValueMember = nameof(CustomerOption.Id);
        _customerComboBox.DataSource = _workspace.Customers
            .OrderBy(customer => customer.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(customer => new CustomerOption(customer.Id, customer.Code, customer.Name, $"{customer.Name} [{customer.Code}]"))
            .ToList();
        _customerComboBox.AutoCompleteCustomSource.AddRange(((IEnumerable<CustomerOption>)_customerComboBox.DataSource!)
            .SelectMany(option => new[]
            {
                option.Name,
                option.Code,
                option.DisplayName
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _customerComboBox.SelectedIndexChanged += (_, _) => RefreshCustomerContext();
        _customerComboBox.Validating += (_, _) => ResolveSelectedCustomerOption();

        _warehouseComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _warehouseComboBox.Items.AddRange(_workspace.Warehouses.Cast<object>().ToArray());
        _warehouseComboBox.SelectedIndexChanged += (_, _) => RefreshStockStatus();

        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(_workspace.OrderStatuses.Cast<object>().ToArray());

        _managerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _managerComboBox.Items.AddRange(_workspace.Managers.Cast<object>().ToArray());

        _commentTextBox.Multiline = true;
        _commentTextBox.ScrollBars = ScrollBars.Vertical;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.White,
            Padding = new Padding(18),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));

        grid.Controls.Add(CreateFieldPanel("Номер", _numberTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Дата", _datePicker), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Покупатель", _customerComboBox), 2, 0);
        grid.Controls.Add(CreateFieldPanel("Склад", _warehouseComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Менеджер", _managerComboBox), 2, 1);
        grid.Controls.Add(CreateContextPanel("Договор", _contractValueLabel), 0, 2);
        grid.Controls.Add(CreateContextPanel("Валюта", _currencyValueLabel), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _commentTextBox), 2, 2);

        return grid;
    }

    private Control CreateLinesSection()
    {
        ConfigureLinesGrid();

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(16)
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52
        };
        header.Controls.Add(new Label
        {
            Text = "Позиции заказа, которые потом станут счетом, резервом и отгрузкой.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Товары",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        });

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 0, 0, 8)
        };

        actions.Controls.Add(CreateLineButton("Добавить позицию", (_, _) => AddLine()));
        actions.Controls.Add(CreateLineButton("Изменить позицию", (_, _) => EditSelectedLine()));
        actions.Controls.Add(CreateLineButton("Удалить позицию", (_, _) => RemoveSelectedLine()));

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(actions, 0, 1);
        root.Controls.Add(_linesGrid, 0, 2);
        panel.Controls.Add(root);
        return panel;
    }

    private void ConfigureLinesGrid()
    {
        _linesGrid.Dock = DockStyle.Fill;
        _linesGrid.AllowUserToAddRows = false;
        _linesGrid.AllowUserToDeleteRows = false;
        _linesGrid.ReadOnly = true;
        _linesGrid.MultiSelect = false;
        _linesGrid.RowHeadersVisible = false;
        _linesGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _linesGrid.AutoGenerateColumns = false;
        _linesGrid.BorderStyle = BorderStyle.None;
        _linesGrid.BackgroundColor = Color.White;
        _linesGrid.GridColor = Color.FromArgb(226, 221, 213);
        _linesGrid.EnableHeadersVisualStyles = false;
        _linesGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(244, 240, 233);
        _linesGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(45, 40, 34);
        _linesGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
        _linesGrid.DefaultCellStyle.BackColor = Color.White;
        _linesGrid.DefaultCellStyle.ForeColor = Color.FromArgb(50, 45, 39);
        _linesGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(230, 236, 250);
        _linesGrid.DefaultCellStyle.SelectionForeColor = Color.FromArgb(27, 34, 45);

        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.ItemCode),
            HeaderText = "Код",
            Width = 110
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.ItemName),
            HeaderText = "Номенклатура",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.Unit),
            HeaderText = "Ед.",
            Width = 70
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.Quantity),
            HeaderText = "Кол-во",
            Width = 110,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.Price),
            HeaderText = "Цена",
            Width = 110,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SalesOrderLineRecord.Amount),
            HeaderText = "Сумма",
            Width = 130,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });

        _linesBindingSource.DataSource = _lines;
        _linesGrid.DataSource = _linesBindingSource;
        _linesGrid.DoubleClick += (_, _) => EditSelectedLine();
    }

    private static Button CreateLineButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 249, 240),
            ForeColor = Color.FromArgb(63, 55, 46),
            Font = new Font("Segoe UI Semibold", 9.5f),
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        button.Click += handler;
        return button;
    }

    private static Control CreateFieldPanel(string label, Control field)
    {
        field.Dock = DockStyle.Fill;
        field.Font = new Font("Segoe UI", 10f);

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        panel.Controls.Add(field);
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 67, 59)
        });
        return panel;
    }

    private static Control CreateContextPanel(string label, Label valueLabel)
    {
        valueLabel.Dock = DockStyle.Fill;
        valueLabel.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(43, 39, 34);
        valueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12)
        };
        panel.Controls.Add(valueLabel);
        panel.Controls.Add(new Label
        {
            Text = label,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 67, 59)
        });
        return panel;
    }

    private Control CreateButtons()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        var totalPanel = new Panel
        {
            Dock = DockStyle.Left,
            Width = 260
        };
        totalPanel.Controls.Add(new Label
        {
            Text = "Итого по заказу",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 67, 59)
        });
        _totalValueLabel.Dock = DockStyle.Top;
        _totalValueLabel.Height = 34;
        _totalValueLabel.Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
        _totalValueLabel.ForeColor = Color.FromArgb(43, 39, 34);
        totalPanel.Controls.Add(_totalValueLabel);
        _stockStatusValueLabel.Dock = DockStyle.Top;
        _stockStatusValueLabel.Height = 28;
        _stockStatusValueLabel.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        totalPanel.Controls.Add(_stockStatusValueLabel);
        _stockHintValueLabel.Dock = DockStyle.Top;
        _stockHintValueLabel.Height = 42;
        _stockHintValueLabel.Font = new Font("Segoe UI", 9f);
        _stockHintValueLabel.ForeColor = Color.FromArgb(96, 88, 79);
        totalPanel.Controls.Add(_stockHintValueLabel);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var saveButton = CreateActionButton("Сохранить заказ", Color.FromArgb(242, 194, 89), Color.FromArgb(42, 36, 29));
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = CreateActionButton("Отмена", Color.White, Color.FromArgb(63, 55, 46));
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        panel.Controls.Add(totalPanel, 0, 0);
        panel.Controls.Add(buttons, 1, 0);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        return panel;
    }

    private static Button CreateActionButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 156,
            Height = 40,
            Margin = new Padding(10, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Segoe UI Semibold", 9.5f),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        return button;
    }

    private void LoadDraft()
    {
        _numberTextBox.Text = _draft.Number;
        _datePicker.Value = _draft.OrderDate == default ? DateTime.Today : _draft.OrderDate;
        _warehouseComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Warehouse) ? _workspace.Warehouses.First() : _draft.Warehouse;
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? _workspace.OrderStatuses.First() : _draft.Status;
        _managerComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Manager) ? _workspace.Managers.First() : _draft.Manager;
        _commentTextBox.Text = _draft.Comment;

        if (_draft.CustomerId != Guid.Empty)
        {
            var selected = ((IEnumerable<CustomerOption>)_customerComboBox.DataSource!)
                .FirstOrDefault(option => option.Id == _draft.CustomerId);
            if (selected is not null)
            {
                _customerComboBox.SelectedItem = selected;
            }
        }

        if (_customerComboBox.SelectedItem is null && _customerComboBox.Items.Count > 0)
        {
            _customerComboBox.SelectedIndex = 0;
        }

        RefreshCustomerContext();
        RefreshTotals();
        RefreshStockStatus();
    }

    private void RefreshCustomerContext()
    {
        var option = ResolveSelectedCustomerOption();
        if (option is null)
        {
            _contractValueLabel.Text = "-";
            _currencyValueLabel.Text = "-";
            return;
        }

        var customer = _workspace.Customers.First(item => item.Id == option.Id);
        _contractValueLabel.Text = string.IsNullOrWhiteSpace(customer.ContractNumber) ? "-" : customer.ContractNumber;
        _currencyValueLabel.Text = customer.CurrencyCode;

        if (string.IsNullOrWhiteSpace(_managerComboBox.Text))
        {
            _managerComboBox.SelectedItem = customer.Manager;
        }
    }

    private void RefreshTotals()
    {
        _linesBindingSource.ResetBindings(false);
        _totalValueLabel.Text = $"{_lines.Sum(line => line.Amount):N2} ₽";
    }

    private void RefreshStockStatus()
    {
        if (_lines.Count == 0)
        {
            _stockStatusValueLabel.Text = "Резерв не рассчитан";
            _stockStatusValueLabel.ForeColor = Color.FromArgb(165, 110, 47);
            _stockHintValueLabel.Text = "Добавьте позиции, чтобы увидеть покрытие остатком по выбранному складу.";
            return;
        }

        var warehouse = _warehouseComboBox.SelectedItem?.ToString() ?? _draft.Warehouse;
        var check = _inventory.AnalyzeDraft(warehouse, _lines, _draft.Id, null);
        _stockStatusValueLabel.Text = check.StatusText;
        _stockStatusValueLabel.ForeColor = check.IsFullyCovered
            ? Color.FromArgb(79, 146, 90)
            : Color.FromArgb(183, 91, 74);
        _stockHintValueLabel.Text = check.HintText;
    }

    private void AddLine()
    {
        using var form = new SalesOrderLineEditorForm(_workspace.CatalogItems);
        if (DialogTabsHost.ShowDialog(form, this) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        _lines.Add(form.ResultLine);
    }

    private void EditSelectedLine()
    {
        if (_linesGrid.CurrentRow?.DataBoundItem is not SalesOrderLineRecord line)
        {
            return;
        }

        using var form = new SalesOrderLineEditorForm(_workspace.CatalogItems, line);
        if (DialogTabsHost.ShowDialog(form, this) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        var index = _lines.IndexOf(line);
        _lines[index] = form.ResultLine;
        RefreshTotals();
    }

    private void RemoveSelectedLine()
    {
        if (_linesGrid.CurrentRow?.DataBoundItem is not SalesOrderLineRecord line)
        {
            return;
        }

        var result = MessageBox.Show(
            this,
            $"Удалить позицию \"{line.ItemName}\"?",
            "Заказ покупателя",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (result == DialogResult.Yes)
        {
            _lines.Remove(line);
        }
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_numberTextBox.Text))
        {
            ShowValidationError("Укажите номер заказа.");
            return;
        }

        var option = ResolveSelectedCustomerOption();
        if (option is null)
        {
            ShowValidationError("Выберите покупателя.");
            return;
        }

        if (_warehouseComboBox.SelectedItem is null || _statusComboBox.SelectedItem is null || _managerComboBox.SelectedItem is null)
        {
            ShowValidationError("Заполните склад, статус и менеджера.");
            return;
        }

        if (_lines.Count == 0)
        {
            ShowValidationError("Добавьте хотя бы одну позицию в заказ.");
            return;
        }

        var customer = _workspace.Customers.First(item => item.Id == option.Id);

        _draft.Number = _numberTextBox.Text.Trim();
        _draft.OrderDate = _datePicker.Value.Date;
        _draft.CustomerId = customer.Id;
        _draft.CustomerCode = customer.Code;
        _draft.CustomerName = customer.Name;
        _draft.ContractNumber = customer.ContractNumber;
        _draft.CurrencyCode = customer.CurrencyCode;
        _draft.Warehouse = _warehouseComboBox.SelectedItem.ToString()!;
        _draft.Status = _statusComboBox.SelectedItem.ToString()!;
        _draft.Manager = _managerComboBox.SelectedItem.ToString()!;
        _draft.Comment = _commentTextBox.Text.Trim();
        _draft.Lines = new BindingList<SalesOrderLineRecord>(_lines.Select(line => line.Clone()).ToList());

        ResultOrder = _draft.Clone();
        DialogResult = DialogResult.OK;
    }

    private void ShowValidationError(string message)
    {
        MessageBox.Show(this, message, "Заказ покупателя", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private CustomerOption? ResolveSelectedCustomerOption()
    {
        if (_customerComboBox.SelectedItem is CustomerOption option)
        {
            return option;
        }

        var text = _customerComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var options = (IEnumerable<CustomerOption>)_customerComboBox.DataSource!;
        var match = options.FirstOrDefault(option =>
            option.Code.Equals(text, StringComparison.OrdinalIgnoreCase)
            || option.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
            || option.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase))
            ?? options.FirstOrDefault(option =>
                option.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
                || option.Name.Contains(text, StringComparison.OrdinalIgnoreCase));

        if (match is not null)
        {
            _customerComboBox.SelectedItem = match;
        }

        return match;
    }

    private sealed record CustomerOption(Guid Id, string Code, string Name, string DisplayName);
}

