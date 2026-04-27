using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class SalesInvoiceEditorForm : Form
{
    private readonly SalesWorkspace _workspace;
    private readonly SalesInvoiceRecord _draft;
    private readonly BindingList<SalesOrderLineRecord> _lines;
    private readonly BindingSource _linesBindingSource = new();
    private readonly TextBox _numberTextBox = new();
    private readonly DateTimePicker _invoiceDatePicker = new();
    private readonly DateTimePicker _dueDatePicker = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly ComboBox _managerComboBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly Label _orderValueLabel = new();
    private readonly Label _customerValueLabel = new();
    private readonly Label _contractValueLabel = new();
    private readonly Label _currencyValueLabel = new();
    private readonly Label _totalValueLabel = new();
    private readonly DataGridView _linesGrid = new();

    public SalesInvoiceEditorForm(SalesWorkspace workspace, SalesInvoiceRecord invoice)
    {
        _workspace = workspace;
        _draft = invoice.Clone();
        _lines = new BindingList<SalesOrderLineRecord>(_draft.Lines.Select(line => line.Clone()).ToList());
        ResultInvoice = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(1080, 720);
        MinimumSize = new Size(1080, 720);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = string.IsNullOrWhiteSpace(invoice.Number)
            ? "Счет на оплату"
            : $"Счет на оплату {invoice.Number}";

        BuildLayout();
        LoadDraft();
    }

    public SalesInvoiceRecord? ResultInvoice { get; private set; }

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
            Text = "Счет на оплату формируется из заказа и сохраняет состав позиций без лишних режимов.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });

        panel.Controls.Add(new Label
        {
            Text = "Счет на оплату",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        return panel;
    }

    private Control CreateHeaderFields()
    {
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(_workspace.InvoiceStatuses.Cast<object>().ToArray());

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
        grid.Controls.Add(CreateFieldPanel("Дата счета", _invoiceDatePicker), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Срок оплаты", _dueDatePicker), 2, 0);
        grid.Controls.Add(CreateContextPanel("Основание", _orderValueLabel), 0, 1);
        grid.Controls.Add(CreateContextPanel("Покупатель", _customerValueLabel), 1, 1);
        grid.Controls.Add(CreateContextPanel("Договор", _contractValueLabel), 2, 1);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Менеджер", _managerComboBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _commentTextBox), 2, 2);

        var footer = new Panel
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(12, 0, 0, 0),
            BackColor = Color.White
        };
        footer.Controls.Add(new Label
        {
            Text = "Валюта",
            Dock = DockStyle.Left,
            Width = 92,
            Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(74, 67, 59),
            TextAlign = ContentAlignment.MiddleLeft
        });
        _currencyValueLabel.Dock = DockStyle.Left;
        _currencyValueLabel.Width = 140;
        _currencyValueLabel.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        _currencyValueLabel.ForeColor = Color.FromArgb(43, 39, 34);
        _currencyValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        footer.Controls.Add(_currencyValueLabel);

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.Controls.Add(grid, 0, 0);
        wrapper.Controls.Add(footer, 0, 1);
        return wrapper;
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
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52
        };
        header.Controls.Add(new Label
        {
            Text = "Позиции счета приходят из заказа и здесь доступны для быстрой проверки перед отправкой клиенту.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Состав счета",
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        });

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_linesGrid, 0, 1);
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
            Text = "Итого по счету",
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

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var saveButton = CreateActionButton("Сохранить счет", Color.FromArgb(242, 194, 89), Color.FromArgb(42, 36, 29));
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
        _invoiceDatePicker.Value = _draft.InvoiceDate == default ? DateTime.Today : _draft.InvoiceDate;
        _dueDatePicker.Value = _draft.DueDate == default ? DateTime.Today.AddDays(3) : _draft.DueDate;
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? _workspace.InvoiceStatuses.First() : _draft.Status;
        _managerComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Manager) ? _workspace.Managers.First() : _draft.Manager;
        _commentTextBox.Text = _draft.Comment;
        _orderValueLabel.Text = string.IsNullOrWhiteSpace(_draft.SalesOrderNumber) ? "-" : _draft.SalesOrderNumber;
        _customerValueLabel.Text = string.IsNullOrWhiteSpace(_draft.CustomerName)
            ? "-"
            : $"{_draft.CustomerName} [{_draft.CustomerCode}]";
        _contractValueLabel.Text = string.IsNullOrWhiteSpace(_draft.ContractNumber) ? "-" : _draft.ContractNumber;
        _currencyValueLabel.Text = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? "-" : _draft.CurrencyCode;
        _totalValueLabel.Text = $"{_lines.Sum(line => line.Amount):N2} ₽";
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_numberTextBox.Text))
        {
            ShowValidationError("Укажите номер счета.");
            return;
        }

        if (_statusComboBox.SelectedItem is null || _managerComboBox.SelectedItem is null)
        {
            ShowValidationError("Заполните статус и менеджера.");
            return;
        }

        if (_lines.Count == 0)
        {
            ShowValidationError("В счете должна быть хотя бы одна позиция.");
            return;
        }

        _draft.Number = _numberTextBox.Text.Trim();
        _draft.InvoiceDate = _invoiceDatePicker.Value.Date;
        _draft.DueDate = _dueDatePicker.Value.Date;
        _draft.Status = _statusComboBox.SelectedItem.ToString()!;
        _draft.Manager = _managerComboBox.SelectedItem.ToString()!;
        _draft.Comment = _commentTextBox.Text.Trim();
        _draft.Lines = new BindingList<SalesOrderLineRecord>(_lines.Select(line => line.Clone()).ToList());

        ResultInvoice = _draft.Clone();
        DialogResult = DialogResult.OK;
    }

    private void ShowValidationError(string message)
    {
        MessageBox.Show(this, message, "Счет на оплату", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

