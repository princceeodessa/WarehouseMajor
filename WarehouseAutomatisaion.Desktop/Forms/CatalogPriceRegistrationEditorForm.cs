using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class CatalogPriceRegistrationEditorForm : Form
{
    private readonly CatalogWorkspace _workspace;
    private readonly CatalogPriceRegistrationRecord _draft;
    private readonly BindingList<CatalogPriceRegistrationLineRecord> _lines;
    private readonly BindingSource _linesBindingSource = new();
    private readonly TextBox _numberTextBox = new();
    private readonly DateTimePicker _datePicker = new();
    private readonly ComboBox _priceTypeComboBox = new();
    private readonly ComboBox _currencyComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly Label _lineCountValueLabel = new();
    private readonly DataGridView _linesGrid = new();

    public CatalogPriceRegistrationEditorForm(
        CatalogWorkspace workspace,
        CatalogPriceRegistrationRecord? document = null)
    {
        _workspace = workspace;
        _draft = document?.Clone() ?? workspace.CreatePriceRegistrationDraft();
        _lines = new BindingList<CatalogPriceRegistrationLineRecord>(_draft.Lines.Select(line => line.Clone()).ToList());
        _lines.ListChanged += (_, _) => RefreshLineSummary();
        ResultDocument = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(1080, 760);
        MinimumSize = new Size(1080, 760);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Text = document is null ? "Документ установки цен" : $"Установка цен {document.Number}";

        BuildLayout();
        LoadDraft();
    }

    public CatalogPriceRegistrationRecord? ResultDocument { get; private set; }

    private void BuildLayout()
    {
        _priceTypeComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _currencyComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _priceTypeComboBox.Items.AddRange(_workspace.PriceTypes.Select(item => item.Name).Cast<object>().ToArray());
        _currencyComboBox.Items.AddRange(_workspace.Currencies.Cast<object>().ToArray());
        _statusComboBox.Items.AddRange(_workspace.PriceRegistrationStatuses.Cast<object>().ToArray());
        _commentTextBox.Multiline = true;
        _commentTextBox.ScrollBars = ScrollBars.Vertical;

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
            Text = "Документ изменения цен по выбранному виду цены. Может работать автономно без 1С.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Установка цен",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateHeaderFields()
    {
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 2,
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
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));

        grid.Controls.Add(CreateFieldPanel("Номер", _numberTextBox), 0, 0);
        grid.Controls.Add(CreateFieldPanel("Дата", _datePicker), 1, 0);
        grid.Controls.Add(CreateFieldPanel("Вид цены", _priceTypeComboBox), 2, 0);
        grid.Controls.Add(CreateFieldPanel("Валюта", _currencyComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Комментарий", _commentTextBox), 2, 1);

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

        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        header.Controls.Add(new Label
        {
            Text = "После проведения документа базовая цена обновляется только для основного вида цены.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Строки изменения цен",
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
        actions.Controls.Add(CreateLineButton("Добавить строку", (_, _) => AddLine()));
        actions.Controls.Add(CreateLineButton("Изменить строку", (_, _) => EditSelectedLine()));
        actions.Controls.Add(CreateLineButton("Удалить строку", (_, _) => RemoveSelectedLine()));
        actions.Controls.Add(CreateSummaryChip());

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
            DataPropertyName = nameof(PriceLineGridRow.ItemCode),
            HeaderText = "Код",
            FillWeight = 18
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PriceLineGridRow.ItemName),
            HeaderText = "Номенклатура",
            FillWeight = 42
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PriceLineGridRow.Unit),
            HeaderText = "Ед.",
            FillWeight = 10
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PriceLineGridRow.PreviousPrice),
            HeaderText = "Старая цена",
            FillWeight = 15,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PriceLineGridRow.NewPrice),
            HeaderText = "Новая цена",
            FillWeight = 15,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });

        foreach (DataGridViewColumn column in _linesGrid.Columns)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.SortMode = DataGridViewColumnSortMode.Automatic;
        }

        _linesGrid.DataSource = _linesBindingSource;
        _linesGrid.DoubleClick += (_, _) => EditSelectedLine();
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

    private Control CreateSummaryChip()
    {
        var panel = new Panel
        {
            Width = 180,
            Height = 34,
            BackColor = Color.FromArgb(255, 250, 241),
            Padding = new Padding(10, 7, 10, 7),
            Margin = new Padding(10, 0, 0, 0)
        };
        panel.Controls.Add(_lineCountValueLabel);
        _lineCountValueLabel.Dock = DockStyle.Fill;
        _lineCountValueLabel.TextAlign = ContentAlignment.MiddleLeft;
        _lineCountValueLabel.Font = new Font("Segoe UI Semibold", 9.2f, FontStyle.Bold);
        _lineCountValueLabel.ForeColor = Color.FromArgb(71, 61, 50);
        return panel;
    }

    private Button CreateLineButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 249, 240),
            ForeColor = Color.FromArgb(63, 55, 46),
            Font = new Font("Segoe UI Semibold", 9f),
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(0, 0, 8, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        button.Click += handler;
        return button;
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
        _numberTextBox.Text = _draft.Number;
        _datePicker.Value = _draft.DocumentDate == default ? DateTime.Today : _draft.DocumentDate;
        _priceTypeComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.PriceTypeName) ? null : _draft.PriceTypeName;
        if (_priceTypeComboBox.SelectedItem is null && _priceTypeComboBox.Items.Count > 0)
        {
            _priceTypeComboBox.SelectedIndex = 0;
        }
        _currencyComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? null : _draft.CurrencyCode;
        if (_currencyComboBox.SelectedItem is null && _currencyComboBox.Items.Count > 0)
        {
            _currencyComboBox.SelectedIndex = 0;
        }
        _statusComboBox.SelectedItem = string.IsNullOrWhiteSpace(_draft.Status) ? null : _draft.Status;
        if (_statusComboBox.SelectedItem is null && _statusComboBox.Items.Count > 0)
        {
            _statusComboBox.SelectedIndex = 0;
        }
        _commentTextBox.Text = _draft.Comment;
        RefreshLineSummary();
        RefreshLineGrid();
    }

    private void RefreshLineSummary()
    {
        _lineCountValueLabel.Text = $"Строк в документе: {_lines.Count:N0}";
    }

    private void RefreshLineGrid()
    {
        _linesBindingSource.DataSource = _lines
            .Select(line => new PriceLineGridRow(
                line.Id,
                line.ItemCode,
                line.ItemName,
                line.Unit,
                line.PreviousPrice,
                line.NewPrice))
            .ToList();
    }

    private void AddLine()
    {
        using var dialog = new CatalogPriceRegistrationLineEditorForm(_workspace.Items.ToList());
        if (DialogTabsHost.ShowDialog(dialog, this) != DialogResult.OK || dialog.ResultLine is null)
        {
            return;
        }

        _lines.Add(dialog.ResultLine);
        RefreshLineGrid();
    }

    private void EditSelectedLine()
    {
        if (_linesGrid.CurrentRow?.DataBoundItem is not PriceLineGridRow row)
        {
            return;
        }

        var line = _lines.FirstOrDefault(item => item.Id == row.Id);
        if (line is null)
        {
            return;
        }

        using var dialog = new CatalogPriceRegistrationLineEditorForm(_workspace.Items.ToList(), line);
        if (DialogTabsHost.ShowDialog(dialog, this) != DialogResult.OK || dialog.ResultLine is null)
        {
            return;
        }

        var index = _lines.IndexOf(line);
        _lines[index] = dialog.ResultLine;
        RefreshLineGrid();
    }

    private void RemoveSelectedLine()
    {
        if (_linesGrid.CurrentRow?.DataBoundItem is not PriceLineGridRow row)
        {
            return;
        }

        var line = _lines.FirstOrDefault(item => item.Id == row.Id);
        if (line is null)
        {
            return;
        }

        _lines.Remove(line);
        RefreshLineGrid();
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_numberTextBox.Text))
        {
            MessageBox.Show(this, "Укажите номер документа.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_lines.Count == 0)
        {
            MessageBox.Show(this, "Добавьте хотя бы одну строку с новой ценой.", "Каталог", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ResultDocument = new CatalogPriceRegistrationRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            Number = _numberTextBox.Text.Trim(),
            DocumentDate = _datePicker.Value.Date,
            PriceTypeName = _priceTypeComboBox.SelectedItem?.ToString() ?? _draft.PriceTypeName,
            CurrencyCode = _currencyComboBox.SelectedItem?.ToString() ?? _draft.CurrencyCode,
            Status = _statusComboBox.SelectedItem?.ToString() ?? _draft.Status,
            Comment = _commentTextBox.Text.Trim(),
            Lines = _lines.Select(item => item.Clone()).ToList()
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record PriceLineGridRow(
        Guid Id,
        string ItemCode,
        string ItemName,
        string Unit,
        decimal PreviousPrice,
        decimal NewPrice);
}
