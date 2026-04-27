using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class PurchasingLineEditorForm : Form
{
    private const decimal MinimumValue = 0.01m;
    private readonly OperationalPurchasingLineRecord _draft;
    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly ComboBox _itemComboBox = new();
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _unitTextBox = new();
    private readonly NumericUpDown _quantityNumeric = new();
    private readonly NumericUpDown _priceNumeric = new();
    private readonly Label _amountLabel = new();

    public PurchasingLineEditorForm(
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        OperationalPurchasingLineRecord? line = null)
    {
        _catalogItems = catalogItems;
        _draft = line?.Clone() ?? new OperationalPurchasingLineRecord { Id = Guid.NewGuid(), SectionName = "Товары" };
        ResultLine = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(620, 360);
        MinimumSize = new Size(620, 360);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = line is null ? "Новая позиция закупки" : "Позиция закупки";

        BuildLayout();
        LoadDraft();
    }

    public OperationalPurchasingLineRecord? ResultLine { get; private set; }

    private void BuildLayout()
    {
        _itemComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _itemComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _itemComboBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _itemComboBox.DisplayMember = nameof(SalesCatalogItemOption.Name);
        _itemComboBox.ValueMember = nameof(SalesCatalogItemOption.Code);
        _itemComboBox.DataSource = _catalogItems
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _itemComboBox.AutoCompleteCustomSource.AddRange(_catalogItems
            .SelectMany(item => new[] { item.Name, item.Code, $"{item.Name} [{item.Code}]" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _itemComboBox.SelectedIndexChanged += (_, _) => ApplySelectedItemDefaults();
        _itemComboBox.Validating += (_, _) => ResolveSelectedItem();

        _codeTextBox.ReadOnly = true;
        _unitTextBox.ReadOnly = true;

        _quantityNumeric.DecimalPlaces = 2;
        _quantityNumeric.Maximum = 1_000_000;
        _quantityNumeric.Minimum = MinimumValue;
        _quantityNumeric.ThousandsSeparator = true;
        _quantityNumeric.ValueChanged += (_, _) => RefreshAmount();

        _priceNumeric.DecimalPlaces = 2;
        _priceNumeric.Maximum = 1_000_000;
        _priceNumeric.Minimum = MinimumValue;
        _priceNumeric.ThousandsSeparator = true;
        _priceNumeric.ValueChanged += (_, _) => RefreshAmount();

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
            Text = "Выберите товар, количество и цену. Сумма пересчитывается автоматически.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Позиция закупки",
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
            RowCount = 3,
            BackColor = Color.White,
            Padding = new Padding(18),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreateFieldPanel("Номенклатура", _itemComboBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Код", _codeTextBox), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Единица", _unitTextBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Количество", _quantityNumeric), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Цена", _priceNumeric), 0, 2);
        grid.Controls.Add(CreateAmountPanel(), 1, 2);

        return grid;
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

    private Control CreateAmountPanel()
    {
        var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12) };
        _amountLabel.Dock = DockStyle.Fill;
        _amountLabel.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
        _amountLabel.ForeColor = Color.FromArgb(43, 39, 34);
        _amountLabel.TextAlign = ContentAlignment.MiddleLeft;

        panel.Controls.Add(_amountLabel);
        panel.Controls.Add(new Label
        {
            Text = "Сумма",
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
        if (!string.IsNullOrWhiteSpace(_draft.ItemCode))
        {
            var selected = _catalogItems.FirstOrDefault(item => item.Code.Equals(_draft.ItemCode, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _itemComboBox.SelectedItem = selected;
            }
        }

        if (_itemComboBox.SelectedItem is null && _itemComboBox.Items.Count > 0)
        {
            _itemComboBox.SelectedIndex = 0;
        }

        if (_draft.Quantity > 0)
        {
            _quantityNumeric.Value = _draft.Quantity;
        }

        if (_draft.Price > 0)
        {
            _priceNumeric.Value = _draft.Price;
        }

        if (!string.IsNullOrWhiteSpace(_draft.ItemCode))
        {
            _codeTextBox.Text = _draft.ItemCode;
        }

        if (!string.IsNullOrWhiteSpace(_draft.Unit))
        {
            _unitTextBox.Text = _draft.Unit;
        }

        RefreshAmount();
    }

    private void ApplySelectedItemDefaults()
    {
        if (_itemComboBox.SelectedItem is not SalesCatalogItemOption item)
        {
            return;
        }

        _codeTextBox.Text = item.Code;
        _unitTextBox.Text = item.Unit;
        if (_draft.Price <= 0 && _priceNumeric.Value <= 0)
        {
            _priceNumeric.Value = item.DefaultPrice;
        }

        RefreshAmount();
    }

    private void ResolveSelectedItem()
    {
        var text = _itemComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var selected = _catalogItems.FirstOrDefault(item =>
            item.Name.Equals(text, StringComparison.OrdinalIgnoreCase)
            || item.Code.Equals(text, StringComparison.OrdinalIgnoreCase)
            || $"{item.Name} [{item.Code}]".Equals(text, StringComparison.OrdinalIgnoreCase));

        if (selected is not null)
        {
            _itemComboBox.SelectedItem = selected;
        }
    }

    private void RefreshAmount()
    {
        var amount = Math.Round(_quantityNumeric.Value * _priceNumeric.Value, 2, MidpointRounding.AwayFromZero);
        _amountLabel.Text = $"{amount:N2} ₽";
    }

    private void SaveAndClose()
    {
        ResolveSelectedItem();
        if (_itemComboBox.SelectedItem is not SalesCatalogItemOption item)
        {
            MessageBox.Show(this, "Выберите номенклатуру.", "Закупки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultLine = new OperationalPurchasingLineRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            SectionName = string.IsNullOrWhiteSpace(_draft.SectionName) ? "Товары" : _draft.SectionName,
            ItemCode = item.Code,
            ItemName = item.Name,
            Quantity = _quantityNumeric.Value,
            Unit = item.Unit,
            Price = _priceNumeric.Value,
            PlannedDate = _draft.PlannedDate,
            RelatedDocument = _draft.RelatedDocument,
            Fields = _draft.Fields.ToArray()
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}

