using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CatalogPriceRegistrationLineEditorForm : Form
{
    private readonly CatalogPriceRegistrationLineRecord _draft;
    private readonly IReadOnlyList<CatalogItemRecord> _items;
    private readonly ComboBox _itemComboBox = new();
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _unitTextBox = new();
    private readonly NumericUpDown _previousPriceNumeric = new();
    private readonly NumericUpDown _newPriceNumeric = new();

    public CatalogPriceRegistrationLineEditorForm(
        IReadOnlyList<CatalogItemRecord> items,
        CatalogPriceRegistrationLineRecord? line = null)
    {
        _items = items;
        _draft = line?.Clone() ?? new CatalogPriceRegistrationLineRecord { Id = Guid.NewGuid() };
        ResultLine = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(700, 380);
        MinimumSize = new Size(700, 380);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = line is null ? "Новая строка цены" : "Строка документа цен";

        BuildLayout();
        LoadDraft();
    }

    public CatalogPriceRegistrationLineRecord? ResultLine { get; private set; }

    private void BuildLayout()
    {
        _itemComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _itemComboBox.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        _itemComboBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
        _itemComboBox.DisplayMember = nameof(CatalogItemRecord.Name);
        _itemComboBox.ValueMember = nameof(CatalogItemRecord.Code);
        _itemComboBox.DataSource = _items
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        _itemComboBox.AutoCompleteCustomSource.AddRange(_items
            .SelectMany(item => new[] { item.Name, item.Code, $"{item.Name} [{item.Code}]" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray());
        _itemComboBox.SelectedIndexChanged += (_, _) => ApplySelectedItemDefaults();
        _itemComboBox.Validating += (_, _) => ResolveSelectedItem();

        _codeTextBox.ReadOnly = true;
        _unitTextBox.ReadOnly = true;
        ConfigureNumeric(_previousPriceNumeric);
        ConfigureNumeric(_newPriceNumeric);

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

    private static void ConfigureNumeric(NumericUpDown control)
    {
        control.DecimalPlaces = 2;
        control.Maximum = 1_000_000_000m;
        control.Minimum = 0m;
        control.ThousandsSeparator = true;
    }

    private Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Выберите товар и зафиксируйте новую цену по выбранному виду цен.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Строка документа цен",
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
        grid.Controls.Add(CreateFieldPanel("Текущая цена", _previousPriceNumeric), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Новая цена", _newPriceNumeric), 0, 2);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 2)!, 2);

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
            var selected = _items.FirstOrDefault(item => item.Code.Equals(_draft.ItemCode, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                _itemComboBox.SelectedItem = selected;
            }
        }

        if (_itemComboBox.SelectedItem is null && _itemComboBox.Items.Count > 0)
        {
            _itemComboBox.SelectedIndex = 0;
        }

        _previousPriceNumeric.Value = _draft.PreviousPrice;
        _newPriceNumeric.Value = _draft.NewPrice > 0m ? _draft.NewPrice : _previousPriceNumeric.Value;
    }

    private void ApplySelectedItemDefaults()
    {
        if (_itemComboBox.SelectedItem is not CatalogItemRecord item)
        {
            return;
        }

        _codeTextBox.Text = item.Code;
        _unitTextBox.Text = item.Unit;
        _previousPriceNumeric.Value = item.DefaultPrice;
        if (_newPriceNumeric.Value <= 0m)
        {
            _newPriceNumeric.Value = item.DefaultPrice;
        }
    }

    private void ResolveSelectedItem()
    {
        if (_itemComboBox.SelectedItem is CatalogItemRecord)
        {
            return;
        }

        var rawValue = _itemComboBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return;
        }

        var resolved = _items.FirstOrDefault(item =>
            item.Code.Equals(rawValue, StringComparison.OrdinalIgnoreCase)
            || item.Name.Equals(rawValue, StringComparison.OrdinalIgnoreCase)
            || $"{item.Name} [{item.Code}]".Equals(rawValue, StringComparison.OrdinalIgnoreCase));
        if (resolved is not null)
        {
            _itemComboBox.SelectedItem = resolved;
        }
    }

    private void SaveAndClose()
    {
        ResolveSelectedItem();
        if (_itemComboBox.SelectedItem is not CatalogItemRecord item)
        {
            MessageBox.Show(this, "Выберите товар из каталога.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_newPriceNumeric.Value <= 0m)
        {
            MessageBox.Show(this, "Укажите новую цену больше нуля.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultLine = new CatalogPriceRegistrationLineRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            ItemCode = item.Code,
            ItemName = item.Name,
            Unit = item.Unit,
            PreviousPrice = _previousPriceNumeric.Value,
            NewPrice = _newPriceNumeric.Value
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
