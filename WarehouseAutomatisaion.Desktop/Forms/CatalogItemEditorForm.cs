using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CatalogItemEditorForm : Form
{
    private readonly CatalogItemRecord _draft;
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _unitTextBox = new();
    private readonly ComboBox _categoryComboBox = new();
    private readonly ComboBox _supplierComboBox = new();
    private readonly ComboBox _warehouseComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly ComboBox _currencyComboBox = new();
    private readonly NumericUpDown _priceNumeric = new();
    private readonly ComboBox _barcodeFormatComboBox = new();
    private readonly TextBox _barcodeValueTextBox = new();
    private readonly TextBox _qrPayloadTextBox = new();
    private readonly TextBox _notesTextBox = new();

    public CatalogItemEditorForm(CatalogWorkspace workspace, CatalogItemRecord? item = null)
    {
        _draft = item?.Clone() ?? workspace.CreateItemDraft();
        ResultItem = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(980, 740);
        MinimumSize = new Size(980, 740);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = item is null ? "Новая карточка номенклатуры" : "Карточка номенклатуры";

        BuildLayout(workspace);
        LoadDraft();
    }

    public CatalogItemRecord? ResultItem { get; private set; }

    private void BuildLayout(CatalogWorkspace workspace)
    {
        _categoryComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _supplierComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _warehouseComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _currencyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _barcodeFormatComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        _categoryComboBox.Items.AddRange(workspace.Items
            .Select(item => item.Category)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Cast<object>()
            .ToArray());
        _supplierComboBox.Items.AddRange(workspace.Items
            .Select(item => item.Supplier)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Cast<object>()
            .ToArray());
        _warehouseComboBox.Items.AddRange(workspace.Warehouses.Cast<object>().ToArray());
        _statusComboBox.Items.AddRange(workspace.ItemStatuses.Cast<object>().ToArray());
        _currencyComboBox.Items.AddRange(workspace.Currencies.Cast<object>().ToArray());
        _barcodeFormatComboBox.Items.AddRange(["Code128", "Code39"]);

        _notesTextBox.Multiline = true;
        _notesTextBox.ScrollBars = ScrollBars.Vertical;
        _barcodeValueTextBox.CharacterCasing = CharacterCasing.Upper;
        _qrPayloadTextBox.Multiline = true;
        _qrPayloadTextBox.ScrollBars = ScrollBars.Vertical;
        _priceNumeric.DecimalPlaces = 2;
        _priceNumeric.Maximum = 1_000_000_000m;
        _priceNumeric.ThousandsSeparator = true;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateFieldsGrid(), 0, 1);
        root.Controls.Add(CreateButtons(), 0, 2);
        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Карточка товара, склад по умолчанию, цена и маркировка без сложной формы 1С.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Номенклатура",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateFieldsGrid()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            BackColor = Color.White,
            Padding = new Padding(18),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var rowIndex = 0; rowIndex < 5; rowIndex++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        }

        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreateFieldPanel("Код", _codeTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Наименование", _nameTextBox), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Единица", _unitTextBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Категория", _categoryComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Поставщик", _supplierComboBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Склад по умолчанию", _warehouseComboBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 0, 3);
        grid.Controls.Add(CreateFieldPanel("Валюта / Цена", CreatePricePanel()), 1, 3);
        grid.Controls.Add(CreateFieldPanel("Формат штрихкода", _barcodeFormatComboBox), 0, 4);
        grid.Controls.Add(CreateFieldPanel("Значение штрихкода", _barcodeValueTextBox), 1, 4);
        grid.Controls.Add(CreateFieldPanel("QR payload", _qrPayloadTextBox), 0, 5);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 5)!, 2);
        grid.Controls.Add(CreateFieldPanel("Примечания", _notesTextBox), 0, 6);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 6)!, 2);

        return grid;
    }

    private Control CreatePricePanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        panel.Controls.Add(_currencyComboBox, 0, 0);
        panel.Controls.Add(_priceNumeric, 1, 0);
        _currencyComboBox.Dock = DockStyle.Fill;
        _priceNumeric.Dock = DockStyle.Fill;
        return panel;
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

    private Control CreateButtons()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 14, 0, 0)
        };

        var saveButton = CreateActionButton("Сохранить", Color.FromArgb(242, 194, 89), Color.FromArgb(42, 36, 29));
        saveButton.Click += (_, _) => SaveAndClose();

        var cancelButton = CreateActionButton("Отмена", Color.White, Color.FromArgb(63, 55, 46));
        cancelButton.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        cancelButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        panel.Controls.Add(saveButton);
        panel.Controls.Add(cancelButton);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
        return panel;
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

    private void LoadDraft()
    {
        _codeTextBox.Text = _draft.Code;
        _nameTextBox.Text = _draft.Name;
        _unitTextBox.Text = _draft.Unit;
        _categoryComboBox.Text = _draft.Category;
        _supplierComboBox.Text = _draft.Supplier;
        _warehouseComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.DefaultWarehouse) ? null : _draft.DefaultWarehouse;
        if (_warehouseComboBox.SelectedItem is null && _warehouseComboBox.Items.Count > 0)
        {
            _warehouseComboBox.SelectedIndex = 0;
        }

        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }

        _currencyComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? null : _draft.CurrencyCode;
        if (_currencyComboBox.SelectedItem is null && _currencyComboBox.Items.Count > 0)
        {
            _currencyComboBox.SelectedIndex = 0;
        }

        _barcodeFormatComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.BarcodeFormat) ? null : _draft.BarcodeFormat;
        if (_barcodeFormatComboBox.SelectedItem is null && _barcodeFormatComboBox.Items.Count > 0)
        {
            _barcodeFormatComboBox.SelectedIndex = 0;
        }

        _priceNumeric.Value = Math.Max(_priceNumeric.Minimum, _draft.DefaultPrice);
        if (_draft.DefaultPrice <= 0m)
        {
            _priceNumeric.Value = 0m;
        }

        _barcodeValueTextBox.Text = _draft.BarcodeValue;
        _qrPayloadTextBox.Text = _draft.QrPayload;
        _notesTextBox.Text = _draft.Notes;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show(this, "Укажите наименование товара.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultItem = new CatalogItemRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = string.IsNullOrWhiteSpace(_codeTextBox.Text) ? _draft.Code : _codeTextBox.Text.Trim(),
            Name = _nameTextBox.Text.Trim(),
            Unit = _unitTextBox.Text.Trim(),
            Category = _categoryComboBox.Text.Trim(),
            Supplier = _supplierComboBox.Text.Trim(),
            DefaultWarehouse = _warehouseComboBox.SelectedItem?.ToString() ?? _draft.DefaultWarehouse,
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status,
            CurrencyCode = _currencyComboBox.SelectedItem?.ToString() ?? _draft.CurrencyCode,
            DefaultPrice = _priceNumeric.Value,
            BarcodeFormat = _barcodeFormatComboBox.SelectedItem?.ToString() ?? _draft.BarcodeFormat,
            BarcodeValue = _barcodeValueTextBox.Text.Trim(),
            QrPayload = _qrPayloadTextBox.Text.Trim(),
            Notes = _notesTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный каталог" : _draft.SourceLabel
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
