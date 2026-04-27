using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CatalogDiscountEditorForm : Form
{
    private readonly CatalogDiscountRecord _draft;
    private readonly TextBox _nameTextBox = new();
    private readonly NumericUpDown _percentNumeric = new();
    private readonly ComboBox _priceTypeComboBox = new();
    private readonly TextBox _periodTextBox = new();
    private readonly TextBox _scopeTextBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _commentTextBox = new();

    public CatalogDiscountEditorForm(CatalogWorkspace workspace, CatalogDiscountRecord? discount = null)
    {
        _draft = discount?.Clone() ?? workspace.CreateDiscountDraft();
        ResultDiscount = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(860, 580);
        MinimumSize = new Size(860, 580);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = discount is null ? "Новая скидка" : "Скидка";

        BuildLayout(workspace);
        LoadDraft();
    }

    public CatalogDiscountRecord? ResultDiscount { get; private set; }

    private void BuildLayout(CatalogWorkspace workspace)
    {
        _priceTypeComboBox.DropDownStyle = ComboBoxStyle.DropDown;
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;

        _priceTypeComboBox.Items.AddRange(workspace.PriceTypes
            .Select(item => item.Name)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCultureIgnoreCase)
            .Cast<object>()
            .ToArray());
        _statusComboBox.Items.AddRange(workspace.DiscountStatuses.Cast<object>().ToArray());
        _percentNumeric.DecimalPlaces = 2;
        _percentNumeric.Maximum = 100m;
        _percentNumeric.Minimum = 0m;
        _percentNumeric.ThousandsSeparator = true;
        _commentTextBox.Multiline = true;
        _commentTextBox.ScrollBars = ScrollBars.Vertical;

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
            Text = "Скидка по виду цены, периоду действия и области применения.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Скидка",
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
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreateFieldPanel("Наименование", _nameTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Процент", _percentNumeric), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Вид цены", _priceTypeComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Период", _periodTextBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Кому / где действует", _scopeTextBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _commentTextBox), 0, 3);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 3)!, 2);

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
        _nameTextBox.Text = _draft.Name;
        _percentNumeric.Value = _draft.Percent;
        _priceTypeComboBox.Text = _draft.PriceTypeName;
        _periodTextBox.Text = _draft.Period;
        _scopeTextBox.Text = _draft.Scope;
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }
        _commentTextBox.Text = _draft.Comment;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show(this, "Укажите наименование скидки.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultDiscount = new CatalogDiscountRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Name = _nameTextBox.Text.Trim(),
            Percent = _percentNumeric.Value,
            PriceTypeName = _priceTypeComboBox.Text.Trim(),
            Period = _periodTextBox.Text.Trim(),
            Scope = _scopeTextBox.Text.Trim(),
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status,
            Comment = _commentTextBox.Text.Trim()
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
