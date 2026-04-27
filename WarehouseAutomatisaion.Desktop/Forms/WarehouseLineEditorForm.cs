using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class WarehouseLineEditorForm : Form
{
    private const decimal PositiveMinimumValue = 0.01m;
    private readonly OperationalWarehouseLineRecord _draft;
    private readonly IReadOnlyList<SalesCatalogItemOption> _catalogItems;
    private readonly bool _allowNegativeQuantity;
    private readonly bool _allowTargetLocation;
    private readonly ComboBox _itemComboBox = new();
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _unitTextBox = new();
    private readonly NumericUpDown _quantityNumeric = new();
    private readonly TextBox _sourceLocationTextBox = new();
    private readonly TextBox _targetLocationTextBox = new();

    public WarehouseLineEditorForm(
        string title,
        string subtitle,
        IReadOnlyList<SalesCatalogItemOption> catalogItems,
        OperationalWarehouseLineRecord? line = null,
        bool allowNegativeQuantity = false,
        bool allowTargetLocation = true)
    {
        _catalogItems = catalogItems;
        _allowNegativeQuantity = allowNegativeQuantity;
        _allowTargetLocation = allowTargetLocation;
        _draft = line?.Clone() ?? new OperationalWarehouseLineRecord { Id = Guid.NewGuid() };
        ResultLine = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(680, 420);
        MinimumSize = new Size(680, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = title;

        BuildLayout(subtitle);
        LoadDraft();
    }

    public OperationalWarehouseLineRecord? ResultLine { get; private set; }

    private void BuildLayout(string subtitle)
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
        _quantityNumeric.Minimum = _allowNegativeQuantity ? -1_000_000 : PositiveMinimumValue;
        _quantityNumeric.ThousandsSeparator = true;

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

        root.Controls.Add(CreateHeader(subtitle), 0, 0);
        root.Controls.Add(CreateFieldsGrid(), 0, 1);
        root.Controls.Add(CreateButtons(), 0, 2);
        Controls.Add(root);
    }

    private Control CreateHeader(string subtitle)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = Text,
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
        grid.Controls.Add(CreateFieldPanel(_allowNegativeQuantity ? "Корректировка (+/-)" : "Количество", _quantityNumeric), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Ячейка / место", _sourceLocationTextBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Место назначения", _targetLocationTextBox), 1, 2);

        if (!_allowTargetLocation)
        {
            _targetLocationTextBox.Enabled = false;
        }

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

        if (_allowNegativeQuantity)
        {
            _quantityNumeric.Value = Math.Min(_quantityNumeric.Maximum, Math.Max(_quantityNumeric.Minimum, _draft.Quantity));
        }
        else if (_draft.Quantity > 0)
        {
            _quantityNumeric.Value = Math.Min(_quantityNumeric.Maximum, _draft.Quantity);
        }

        _sourceLocationTextBox.Text = _draft.SourceLocation;
        _targetLocationTextBox.Text = _draft.TargetLocation;
        ApplySelectedItemDefaults();
    }

    private void ApplySelectedItemDefaults()
    {
        if (_itemComboBox.SelectedItem is not SalesCatalogItemOption selected)
        {
            return;
        }

        _codeTextBox.Text = selected.Code;
        _unitTextBox.Text = selected.Unit;
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
            return;
        }

        var matchedIndex = _catalogItems
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => pair.item.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || pair.item.Code.Contains(text, StringComparison.OrdinalIgnoreCase))
            .index;
        if (matchedIndex >= 0 && matchedIndex < _itemComboBox.Items.Count)
        {
            _itemComboBox.SelectedIndex = matchedIndex;
        }
    }

    private void SaveAndClose()
    {
        ResolveSelectedItem();
        if (_itemComboBox.SelectedItem is not SalesCatalogItemOption selectedItem)
        {
            MessageBox.Show(this, "Выберите номенклатуру из каталога.", "Позиция склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!_allowNegativeQuantity && _quantityNumeric.Value <= 0)
        {
            MessageBox.Show(this, "Количество должно быть больше нуля.", "Позиция склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_allowNegativeQuantity && _quantityNumeric.Value == 0)
        {
            MessageBox.Show(this, "Корректировка не может быть нулевой.", "Позиция склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ResultLine = new OperationalWarehouseLineRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            ItemCode = selectedItem.Code,
            ItemName = selectedItem.Name,
            Unit = selectedItem.Unit,
            Quantity = _quantityNumeric.Value,
            SourceLocation = _sourceLocationTextBox.Text.Trim(),
            TargetLocation = _allowTargetLocation ? _targetLocationTextBox.Text.Trim() : string.Empty,
            RelatedDocument = _draft.RelatedDocument,
            Fields = _draft.Fields.ToArray()
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
