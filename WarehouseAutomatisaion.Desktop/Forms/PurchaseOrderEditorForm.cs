using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class PurchaseOrderEditorForm : Form
{
    private readonly OperationalPurchasingWorkspace _workspace;
    private readonly OperationalPurchasingDocumentRecord _draft;
    private readonly BindingList<OperationalPurchasingLineRecord> _lines;
    private readonly BindingSource _linesBindingSource = new();
    private readonly TextBox _numberTextBox = new();
    private readonly DateTimePicker _datePicker = new();
    private readonly ComboBox _supplierComboBox = new();
    private readonly ComboBox _warehouseComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _contractTextBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly Label _totalValueLabel = new();
    private readonly DataGridView _linesGrid = new();

    public PurchaseOrderEditorForm(
        OperationalPurchasingWorkspace workspace,
        OperationalPurchasingDocumentRecord? order = null,
        Guid? preselectedSupplierId = null)
    {
        _workspace = workspace;
        _draft = order?.Clone() ?? workspace.CreatePurchaseOrderDraft(preselectedSupplierId);
        _lines = new BindingList<OperationalPurchasingLineRecord>(_draft.Lines.Select(line => line.Clone()).ToList());
        _lines.ListChanged += (_, _) => RefreshTotals();
        ResultDocument = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = order is null ? "Заказ поставщику" : $"Заказ поставщику {order.Number}";

        BuildLayout();
        LoadDraft();
    }

    public OperationalPurchasingDocumentRecord? ResultDocument { get; private set; }

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
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Поставщик, склад, договор и состав закупки в одном окне.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Заказ поставщику",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateHeaderFields()
    {
        _supplierComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _supplierComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _supplierComboBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _supplierComboBox.DisplayMember = nameof(SupplierOption.DisplayName);
        _supplierComboBox.ValueMember = nameof(SupplierOption.Id);
        _supplierComboBox.DataSource = _workspace.Suppliers
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new SupplierOption(item.Id, item.Code, item.Name, $"{item.Name} [{item.Code}]"))
            .ToList();
        _supplierComboBox.AutoCompleteCustomSource.AddRange(((IEnumerable<SupplierOption>)_supplierComboBox.DataSource!)
            .SelectMany(option => new[] { option.Name, option.Code, option.DisplayName })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _supplierComboBox.SelectedIndexChanged += (_, _) => RefreshSupplierContext();
        _supplierComboBox.Validating += (_, _) => ResolveSelectedSupplier();

        _warehouseComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _warehouseComboBox.Items.AddRange(_workspace.Warehouses.Cast<object>().ToArray());

        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(_workspace.PurchaseOrderStatuses.Cast<object>().ToArray());

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
        grid.Controls.Add(CreateFieldPanel("Поставщик", _supplierComboBox), 2, 0);
        grid.Controls.Add(CreateFieldPanel("Склад", _warehouseComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Договор", _contractTextBox), 2, 1);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _commentTextBox), 0, 2);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 2)!, 3);

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

        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Controls.Add(new Label
        {
            Text = "Позиции заказа станут основанием для счета поставщика и приемки.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Позиции закупки",
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
            DataPropertyName = nameof(LineGridRow.ItemCode),
            HeaderText = "Код",
            FillWeight = 18
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.ItemName),
            HeaderText = "Номенклатура",
            FillWeight = 42
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.Unit),
            HeaderText = "Ед.",
            FillWeight = 10
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.Quantity),
            HeaderText = "Количество",
            FillWeight = 12,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.Price),
            HeaderText = "Цена",
            FillWeight = 12,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.Amount),
            HeaderText = "Сумма",
            FillWeight = 14,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });

        _linesBindingSource.DataSource = Array.Empty<LineGridRow>();
        _linesGrid.DataSource = _linesBindingSource;
        _linesGrid.DoubleClick += (_, _) => EditSelectedLine();
    }

    private Control CreateButtons()
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(0, 14, 0, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        _totalValueLabel.Dock = DockStyle.Fill;
        _totalValueLabel.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        _totalValueLabel.ForeColor = Color.FromArgb(43, 39, 34);
        _totalValueLabel.TextAlign = ContentAlignment.MiddleLeft;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };

        var saveButton = CreateActionButton("Сохранить", Color.FromArgb(242, 194, 89), Color.FromArgb(42, 36, 29));
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = CreateActionButton("Отмена", Color.White, Color.FromArgb(63, 55, 46));
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        layout.Controls.Add(_totalValueLabel, 0, 0);
        layout.Controls.Add(buttons, 1, 0);

        AcceptButton = saveButton;
        CancelButton = cancelButton;
        return layout;
    }

    private static Button CreateActionButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 138,
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
            Margin = new Padding(10, 0, 0, 0),
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

        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
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

    private void LoadDraft()
    {
        _numberTextBox.Text = _draft.Number;
        _datePicker.Value = _draft.DocumentDate == default ? DateTime.Today : _draft.DocumentDate;
        _warehouseComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Warehouse) ? null : _draft.Warehouse;
        if (_warehouseComboBox.SelectedItem is null && _warehouseComboBox.Items.Count > 0)
        {
            _warehouseComboBox.SelectedIndex = 0;
        }

        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }

        if (_draft.SupplierId != Guid.Empty)
        {
            foreach (SupplierOption option in _supplierComboBox.Items)
            {
                if (option.Id == _draft.SupplierId)
                {
                    _supplierComboBox.SelectedItem = option;
                    break;
                }
            }
        }

        if (_supplierComboBox.SelectedItem is null && _supplierComboBox.Items.Count > 0)
        {
            _supplierComboBox.SelectedIndex = 0;
        }

        _contractTextBox.Text = _draft.Contract;
        _commentTextBox.Text = _draft.Comment;
        RefreshSupplierContext();
        RefreshLines();
        RefreshTotals();
    }

    private void ResolveSelectedSupplier()
    {
        var text = _supplierComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var selected = _workspace.Suppliers
            .Select(item => new SupplierOption(item.Id, item.Code, item.Name, $"{item.Name} [{item.Code}]"))
            .FirstOrDefault(item =>
                item.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
                || item.Code.Equals(text, StringComparison.OrdinalIgnoreCase)
                || item.DisplayName.Equals(text, StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            _supplierComboBox.SelectedItem = selected;
        }
    }

    private void RefreshSupplierContext()
    {
        ResolveSelectedSupplier();
        if (_supplierComboBox.SelectedItem is not SupplierOption option)
        {
            return;
        }

        var supplier = _workspace.Suppliers.FirstOrDefault(item => item.Id == option.Id);
        if (supplier is not null && string.IsNullOrWhiteSpace(_contractTextBox.Text))
        {
            _contractTextBox.Text = supplier.Contract;
        }
    }

    private void AddLine()
    {
        using var form = new PurchasingLineEditorForm(_workspace.CatalogItems);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        _lines.Add(form.ResultLine);
        RefreshLines(form.ResultLine.Id);
        RefreshTotals();
    }

    private void EditSelectedLine()
    {
        var line = GetSelectedLine();
        if (line is null)
        {
            return;
        }

        using var form = new PurchasingLineEditorForm(_workspace.CatalogItems, line);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        var existing = _lines.First(item => item.Id == line.Id);
        existing.ItemCode = form.ResultLine.ItemCode;
        existing.ItemName = form.ResultLine.ItemName;
        existing.Quantity = form.ResultLine.Quantity;
        existing.Unit = form.ResultLine.Unit;
        existing.Price = form.ResultLine.Price;
        RefreshLines(existing.Id);
        RefreshTotals();
    }

    private void RemoveSelectedLine()
    {
        var line = GetSelectedLine();
        if (line is null)
        {
            return;
        }

        _lines.Remove(line);
        RefreshLines();
        RefreshTotals();
    }

    private void RefreshLines(Guid? selectedLineId = null)
    {
        _linesBindingSource.DataSource = _lines
            .Select(line => new LineGridRow(line.Id, line.ItemCode, line.ItemName, line.Unit, line.Quantity, line.Price, line.Amount))
            .ToArray();

        if (_linesGrid.Rows.Count == 0)
        {
            return;
        }

        foreach (DataGridViewRow row in _linesGrid.Rows)
        {
            if (row.DataBoundItem is LineGridRow data && selectedLineId is not null && data.LineId == selectedLineId.Value)
            {
                row.Selected = true;
                _linesGrid.CurrentCell = row.Cells[0];
                return;
            }
        }

        _linesGrid.Rows[0].Selected = true;
        _linesGrid.CurrentCell = _linesGrid.Rows[0].Cells[0];
    }

    private OperationalPurchasingLineRecord? GetSelectedLine()
    {
        return _linesGrid.CurrentRow?.DataBoundItem is LineGridRow row
            ? _lines.FirstOrDefault(item => item.Id == row.LineId)
            : null;
    }

    private void RefreshTotals()
    {
        _totalValueLabel.Text = $"Сумма заказа: {_lines.Sum(item => item.Amount):N2} ₽";
    }

    private void SaveAndClose()
    {
        ResolveSelectedSupplier();
        if (_supplierComboBox.SelectedItem is not SupplierOption option)
        {
            MessageBox.Show(this, "Выберите поставщика.", "Закупки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_lines.Count == 0)
        {
            MessageBox.Show(this, "Добавьте хотя бы одну позицию.", "Закупки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultDocument = new OperationalPurchasingDocumentRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            DocumentType = "Заказ поставщику",
            Number = _numberTextBox.Text.Trim(),
            DocumentDate = _datePicker.Value.Date,
            SupplierId = option.Id,
            SupplierName = option.Name,
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status,
            Contract = _contractTextBox.Text.Trim(),
            Warehouse = _warehouseComboBox.SelectedItem?.ToString() ?? string.Empty,
            RelatedOrderId = _draft.RelatedOrderId,
            RelatedOrderNumber = _draft.RelatedOrderNumber,
            Comment = _commentTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный контур" : _draft.SourceLabel,
            Fields = _draft.Fields.ToArray(),
            Lines = new BindingList<OperationalPurchasingLineRecord>(_lines.Select(item => item.Clone()).ToList())
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record SupplierOption(Guid Id, string Code, string Name, string DisplayName);

    private sealed record LineGridRow(
        [property: Browsable(false)] Guid LineId,
        [property: DisplayName("Код")] string ItemCode,
        [property: DisplayName("Номенклатура")] string ItemName,
        [property: DisplayName("Ед.")] string Unit,
        [property: DisplayName("Количество")] decimal Quantity,
        [property: DisplayName("Цена")] decimal Price,
        [property: DisplayName("Сумма")] decimal Amount);
}
