using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CatalogPriceTypeEditorForm : Form
{
    private readonly CatalogPriceTypeRecord _draft;
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _nameTextBox = new();
    private readonly ComboBox _currencyComboBox = new();
    private readonly ComboBox _basePriceTypeComboBox = new();
    private readonly ComboBox _roundingComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly CheckBox _defaultCheckBox = new();
    private readonly CheckBox _manualEntryCheckBox = new();
    private readonly CheckBox _psychologicalRoundingCheckBox = new();

    public CatalogPriceTypeEditorForm(CatalogWorkspace workspace, CatalogPriceTypeRecord? priceType = null)
    {
        _draft = priceType?.Clone() ?? workspace.CreatePriceTypeDraft();
        ResultPriceType = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(820, 560);
        MinimumSize = new Size(820, 560);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = priceType is null ? "Новый вид цены" : "Вид цены";

        BuildLayout(workspace);
        LoadDraft();
    }

    public CatalogPriceTypeRecord? ResultPriceType { get; private set; }

    private void BuildLayout(CatalogWorkspace workspace)
    {
        _currencyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _basePriceTypeComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _roundingComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        _currencyComboBox.Items.AddRange(workspace.Currencies.Cast<object>().ToArray());
        _basePriceTypeComboBox.Items.AddRange(workspace.PriceTypes
            .Select(item => item.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Cast<object>()
            .ToArray());
        _roundingComboBox.Items.AddRange(new object[] { "Без округления", "Психологическое", "По шагу" });
        _statusComboBox.Items.AddRange(new object[] { "Рабочий", "Ручной", "Архив" });

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
            Text = "Базовый вид цены, валюта и правило округления в одном окне.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Вид цены",
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
            RowCount = 4,
            BackColor = Color.White,
            Padding = new Padding(18),
            CellBorderStyle = TableLayoutPanelCellBorderStyle.Single
        };

        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var rowIndex = 0; rowIndex < 3; rowIndex++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        }
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreateFieldPanel("Код", _codeTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Наименование", _nameTextBox), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Валюта", _currencyComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Базовый вид цены", _basePriceTypeComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Округление", _roundingComboBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Флаги", CreateFlagsPanel()), 0, 3);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 3)!, 2);

        return grid;
    }

    private Control CreateFlagsPanel()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(0, 6, 0, 0)
        };

        ConfigureCheckBox(_defaultCheckBox, "Использовать как основную цену продаж");
        ConfigureCheckBox(_manualEntryCheckBox, "Только ручной ввод");
        ConfigureCheckBox(_psychologicalRoundingCheckBox, "Психологическое округление");

        panel.Controls.Add(_defaultCheckBox);
        panel.Controls.Add(_manualEntryCheckBox);
        panel.Controls.Add(_psychologicalRoundingCheckBox);
        return panel;
    }

    private static void ConfigureCheckBox(CheckBox checkBox, string text)
    {
        checkBox.Text = text;
        checkBox.AutoSize = true;
        checkBox.Font = new Font("Segoe UI", 9.5f);
        checkBox.ForeColor = Color.FromArgb(63, 55, 46);
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
        _currencyComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? null : _draft.CurrencyCode;
        if (_currencyComboBox.SelectedItem is null && _currencyComboBox.Items.Count > 0)
        {
            _currencyComboBox.SelectedIndex = 0;
        }
        _basePriceTypeComboBox.Text = _draft.BasePriceTypeName;
        _roundingComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.RoundingRule) ? null : _draft.RoundingRule;
        if (_roundingComboBox.SelectedItem is null && _roundingComboBox.Items.Count > 0)
        {
            _roundingComboBox.SelectedIndex = 0;
        }
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }
        _defaultCheckBox.Checked = _draft.IsDefault;
        _manualEntryCheckBox.Checked = _draft.IsManualEntryOnly;
        _psychologicalRoundingCheckBox.Checked = _draft.UsesPsychologicalRounding;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show(this, "Укажите наименование вида цены.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultPriceType = new CatalogPriceTypeRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = string.IsNullOrWhiteSpace(_codeTextBox.Text) ? _draft.Code : _codeTextBox.Text.Trim(),
            Name = _nameTextBox.Text.Trim(),
            CurrencyCode = _currencyComboBox.SelectedItem?.ToString() ?? _draft.CurrencyCode,
            BasePriceTypeName = _basePriceTypeComboBox.Text.Trim(),
            RoundingRule = _roundingComboBox.SelectedItem?.ToString() ?? _draft.RoundingRule,
            IsDefault = _defaultCheckBox.Checked,
            IsManualEntryOnly = _manualEntryCheckBox.Checked,
            UsesPsychologicalRounding = _psychologicalRoundingCheckBox.Checked,
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
