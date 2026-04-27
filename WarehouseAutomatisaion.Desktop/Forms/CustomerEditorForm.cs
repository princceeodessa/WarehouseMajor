using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CustomerEditorForm : Form
{
    private readonly SalesCustomerRecord _draft;
    private readonly TextBox _codeTextBox = new();
    private readonly TextBox _nameTextBox = new();
    private readonly TextBox _contractTextBox = new();
    private readonly ComboBox _currencyComboBox = new();
    private readonly ComboBox _managerComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _phoneTextBox = new();
    private readonly TextBox _emailTextBox = new();
    private readonly TextBox _notesTextBox = new();

    public CustomerEditorForm(
        SalesWorkspace workspace,
        SalesCustomerRecord? customer = null)
    {
        _draft = customer?.Clone() ?? workspace.CreateCustomerDraft();
        ResultCustomer = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(760, 540);
        MinimumSize = new Size(760, 540);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = customer is null ? "Новый покупатель" : "Карточка покупателя";

        BuildLayout(workspace);
        LoadDraft();
    }

    public SalesCustomerRecord? ResultCustomer { get; private set; }

    private void BuildLayout(SalesWorkspace workspace)
    {
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
        root.Controls.Add(CreateFieldsGrid(workspace), 0, 1);
        root.Controls.Add(CreateButtons(), 0, 2);
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
            Text = "Контрагент, договор, валюта, менеджер и контактные данные.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });

        panel.Controls.Add(new Label
        {
            Text = "Покупатель",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        return panel;
    }

    private Control CreateFieldsGrid(SalesWorkspace workspace)
    {
        _currencyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _currencyComboBox.Items.AddRange(workspace.Currencies.Cast<object>().ToArray());

        _managerComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _managerComboBox.Items.AddRange(workspace.Managers.Cast<object>().ToArray());

        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(workspace.CustomerStatuses.Cast<object>().ToArray());

        _notesTextBox.Multiline = true;
        _notesTextBox.ScrollBars = ScrollBars.Vertical;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 5,
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

        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(CreateFieldPanel("Код", _codeTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Наименование", _nameTextBox), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Договор", _contractTextBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Валюта", _currencyComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Менеджер", _managerComboBox), 0, 2);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 2);
        grid.Controls.Add(CreateFieldPanel("Телефон", _phoneTextBox), 0, 3);
        grid.Controls.Add(CreateFieldPanel("Email", _emailTextBox), 1, 3);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _notesTextBox), 0, 4);
        grid.SetColumnSpan(grid.GetControlFromPosition(0, 4)!, 2);

        return grid;
    }

    private static Control CreateFieldPanel(string label, Control field)
    {
        field.Dock = DockStyle.Fill;
        field.Font = new Font("Segoe UI", 10f);
        field.Margin = new Padding(0);

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
        _contractTextBox.Text = _draft.ContractNumber;
        _currencyComboBox.SelectedItem = _draft.CurrencyCode;
        _managerComboBox.SelectedItem = _draft.Manager;
        _statusComboBox.SelectedItem = _draft.Status;
        _phoneTextBox.Text = _draft.Phone;
        _emailTextBox.Text = _draft.Email;
        _notesTextBox.Text = _draft.Notes;
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_codeTextBox.Text))
        {
            ShowValidationError("Укажите код покупателя.");
            return;
        }

        if (string.IsNullOrWhiteSpace(_nameTextBox.Text))
        {
            ShowValidationError("Укажите наименование покупателя.");
            return;
        }

        if (_currencyComboBox.SelectedItem is null || _managerComboBox.SelectedItem is null || _statusComboBox.SelectedItem is null)
        {
            ShowValidationError("Заполните валюту, менеджера и статус.");
            return;
        }

        _draft.Code = _codeTextBox.Text.Trim();
        _draft.Name = _nameTextBox.Text.Trim();
        _draft.ContractNumber = _contractTextBox.Text.Trim();
        _draft.CurrencyCode = _currencyComboBox.SelectedItem.ToString()!;
        _draft.Manager = _managerComboBox.SelectedItem.ToString()!;
        _draft.Status = _statusComboBox.SelectedItem.ToString()!;
        _draft.Phone = _phoneTextBox.Text.Trim();
        _draft.Email = _emailTextBox.Text.Trim();
        _draft.Notes = _notesTextBox.Text.Trim();

        ResultCustomer = _draft.Clone();
        DialogResult = DialogResult.OK;
    }

    private void ShowValidationError(string message)
    {
        MessageBox.Show(
            this,
            message,
            "Покупатель",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }
}
