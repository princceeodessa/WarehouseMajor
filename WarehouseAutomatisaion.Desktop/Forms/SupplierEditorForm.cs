using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class SupplierEditorForm : Form
{
    private readonly OperationalPurchasingSupplierRecord _draft;
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _nameTextBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _taxIdTextBox = new();
    private readonly TextBox _phoneTextBox = new();
    private readonly TextBox _emailTextBox = new();
    private readonly TextBox _contractTextBox = new();

    public SupplierEditorForm(OperationalPurchasingWorkspace workspace, OperationalPurchasingSupplierRecord? supplier = null)
    {
        _draft = supplier?.Clone() ?? workspace.CreateSupplierDraft();
        ResultSupplier = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(760, 500);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = supplier is null ? "Новый поставщик" : "Карточка поставщика";

        BuildLayout(workspace);
        LoadDraft();
    }

    public OperationalPurchasingSupplierRecord? ResultSupplier { get; private set; }

    private void BuildLayout(OperationalPurchasingWorkspace workspace)
    {
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(workspace.SupplierStatuses.Cast<object>().ToArray());

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
            Text = "Поставщик, договор, налоговые данные и контакты для закупочного контура.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Поставщик",
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
        for (var rowIndex = 0; rowIndex < 4; rowIndex++)
        {
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        }

        grid.Controls.Add(CreateFieldPanel("Код", _codeTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Наименование", _nameTextBox), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("ИНН / КПП", _taxIdTextBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Телефон", _phoneTextBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Email", _emailTextBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Договор", _contractTextBox), 0, 3);
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
        _codeTextBox.Text = _draft.Code;
        _nameTextBox.Text = _draft.Name;
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }
        _taxIdTextBox.Text = _draft.TaxId;
        _phoneTextBox.Text = _draft.Phone;
        _emailTextBox.Text = _draft.Email;
        _contractTextBox.Text = _draft.Contract;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            MessageBox.Show(this, "Укажите наименование поставщика.", "Закупки", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultSupplier = new OperationalPurchasingSupplierRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Code = string.IsNullOrWhiteSpace(_codeTextBox.Text) ? _draft.Code : _codeTextBox.Text.Trim(),
            Name = _nameTextBox.Text.Trim(),
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status,
            TaxId = _taxIdTextBox.Text.Trim(),
            Phone = _phoneTextBox.Text.Trim(),
            Email = _emailTextBox.Text.Trim(),
            Contract = _contractTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный контур" : _draft.SourceLabel,
            Fields = _draft.Fields.ToArray()
        };

        DialogResult = DialogResult.OK;
        Close();
    }
}
