using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class WarehouseDocumentEditorForm : Form
{
    private readonly OperationalWarehouseWorkspace _workspace;
    private readonly WarehouseDocumentEditorMode _mode;
    private readonly OperationalWarehouseDocumentRecord _draft;
    private readonly BindingList<OperationalWarehouseLineRecord> _lines;
    private readonly BindingSource _linesBindingSource = new();
    private readonly TextBox _numberTextBox = new();
    private readonly DateTimePicker _datePicker = new();
    private readonly ComboBox _sourceWarehouseComboBox = new();
    private readonly ComboBox _targetWarehouseComboBox = new();
    private readonly ComboBox _statusComboBox = new();
    private readonly TextBox _relatedDocumentTextBox = new();
    private readonly TextBox _commentTextBox = new();
    private readonly Label _totalValueLabel = new();
    private readonly DataGridView _linesGrid = new();

    public WarehouseDocumentEditorForm(
        OperationalWarehouseWorkspace workspace,
        WarehouseDocumentEditorMode mode,
        OperationalWarehouseDocumentRecord? document = null)
    {
        _workspace = workspace;
        _mode = mode;
        _draft = document?.Clone() ?? CreateDraft(workspace, mode);
        _lines = new BindingList<OperationalWarehouseLineRecord>(_draft.Lines.Select(line => line.Clone()).ToList());
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
        Text = BuildTitle(document);

        BuildLayout();
        LoadDraft();
    }

    public OperationalWarehouseDocumentRecord? ResultDocument { get; private set; }

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
            Text = BuildSubtitle(),
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = BuildHeaderCaption(),
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateHeaderFields()
    {
        _sourceWarehouseComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _sourceWarehouseComboBox.Items.AddRange(_workspace.Warehouses.Cast<object>().ToArray());

        _targetWarehouseComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _targetWarehouseComboBox.Items.AddRange(_workspace.Warehouses.Cast<object>().ToArray());
        _targetWarehouseComboBox.Enabled = _mode == WarehouseDocumentEditorMode.Transfer;

        _statusComboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _statusComboBox.Items.AddRange(GetStatuses().Cast<object>().ToArray());

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
        grid.Controls.Add(CreateFieldPanel("Статус", _statusComboBox), 2, 0);
        grid.Controls.Add(CreateFieldPanel("Склад-источник", _sourceWarehouseComboBox), 0, 1);
        grid.Controls.Add(CreateFieldPanel(_mode == WarehouseDocumentEditorMode.Transfer ? "Склад-получатель" : "Склад-получатель", _targetWarehouseComboBox), 1, 1);
        grid.Controls.Add(CreateFieldPanel("Основание / ссылка", _relatedDocumentTextBox), 2, 1);
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
            Text = BuildLinesSubtitle(),
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Позиции документа",
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
            FillWeight = 40
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
            HeaderText = _mode == WarehouseDocumentEditorMode.Inventory ? "Корректировка" : "Количество",
            FillWeight = 14,
            DefaultCellStyle = new DataGridViewCellStyle { Format = "N2" }
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.SourceLocation),
            HeaderText = "Источник",
            FillWeight = 18
        });
        _linesGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(LineGridRow.TargetLocation),
            HeaderText = "Назначение",
            FillWeight = 18
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
        SelectComboValue(_sourceWarehouseComboBox, _draft.SourceWarehouse);
        SelectComboValue(_targetWarehouseComboBox, _draft.TargetWarehouse);
        SelectComboValue(_statusComboBox, _draft.Status);
        _relatedDocumentTextBox.Text = _draft.RelatedDocument;
        _commentTextBox.Text = _draft.Comment;
        RefreshLines();
    }

    private void RefreshLines(Guid? selectedLineId = null)
    {
        _linesBindingSource.DataSource = _lines
            .Select(item => new LineGridRow(item))
            .ToArray();

        if (selectedLineId is not null)
        {
            foreach (DataGridViewRow row in _linesGrid.Rows)
            {
                if ((row.DataBoundItem as LineGridRow)?.LineId == selectedLineId)
                {
                    row.Selected = true;
                    _linesGrid.CurrentCell = row.Cells[0];
                    break;
                }
            }
        }

        RefreshTotals();
    }

    private void AddLine()
    {
        using var form = new WarehouseLineEditorForm(
            BuildLineTitle(),
            BuildLineSubtitle(),
            _workspace.CatalogItems,
            allowNegativeQuantity: _mode == WarehouseDocumentEditorMode.Inventory,
            allowTargetLocation: _mode == WarehouseDocumentEditorMode.Transfer);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        _lines.Add(form.ResultLine);
        RefreshLines(form.ResultLine.Id);
    }

    private void EditSelectedLine()
    {
        var line = GetSelectedLine();
        if (line is null)
        {
            MessageBox.Show(this, "Сначала выберите строку.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new WarehouseLineEditorForm(
            BuildLineTitle(),
            BuildLineSubtitle(),
            _workspace.CatalogItems,
            line,
            allowNegativeQuantity: _mode == WarehouseDocumentEditorMode.Inventory,
            allowTargetLocation: _mode == WarehouseDocumentEditorMode.Transfer);
        if (DialogTabsHost.ShowDialog(form, FindForm()) != DialogResult.OK || form.ResultLine is null)
        {
            return;
        }

        var index = _lines.IndexOf(line);
        _lines[index] = form.ResultLine;
        RefreshLines(form.ResultLine.Id);
    }

    private void RemoveSelectedLine()
    {
        var line = GetSelectedLine();
        if (line is null)
        {
            MessageBox.Show(this, "Сначала выберите строку.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (MessageBox.Show(this, "Удалить выбранную позицию?", "Документ склада", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _lines.Remove(line);
        RefreshLines();
    }

    private OperationalWarehouseLineRecord? GetSelectedLine()
    {
        return (_linesGrid.CurrentRow?.DataBoundItem as LineGridRow)?.Source;
    }

    private void RefreshTotals()
    {
        var total = _lines.Sum(item => item.Quantity);
        _totalValueLabel.Text = _mode == WarehouseDocumentEditorMode.Inventory
            ? $"Итоговая корректировка: {total:N2}"
            : $"Всего к движению: {total:N2}";
    }

    private void SaveAndClose()
    {
        var sourceWarehouse = _sourceWarehouseComboBox.SelectedItem?.ToString() ?? string.Empty;
        var targetWarehouse = _targetWarehouseComboBox.SelectedItem?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_numberTextBox.Text))
        {
            MessageBox.Show(this, "Укажите номер документа.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceWarehouse))
        {
            MessageBox.Show(this, "Укажите склад-источник.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_mode == WarehouseDocumentEditorMode.Transfer)
        {
            if (string.IsNullOrWhiteSpace(targetWarehouse))
            {
                MessageBox.Show(this, "Укажите склад-получатель.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (sourceWarehouse.Equals(targetWarehouse, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(this, "Склад-источник и склад-получатель должны отличаться.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
        }

        if (_lines.Count == 0)
        {
            MessageBox.Show(this, "Добавьте хотя бы одну позицию.", "Документ склада", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ResultDocument = new OperationalWarehouseDocumentRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            DocumentType = _draft.DocumentType,
            Number = _numberTextBox.Text.Trim(),
            DocumentDate = _datePicker.Value.Date,
            Status = _statusComboBox.SelectedItem?.ToString() ?? GetStatuses().First(),
            SourceWarehouse = sourceWarehouse,
            TargetWarehouse = _mode == WarehouseDocumentEditorMode.Transfer ? targetWarehouse : string.Empty,
            RelatedDocument = _relatedDocumentTextBox.Text.Trim(),
            Comment = _commentTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный контур" : _draft.SourceLabel,
            Fields = _draft.Fields.ToArray(),
            Lines = new BindingList<OperationalWarehouseLineRecord>(_lines.Select(item => item.Clone()).ToList())
        };

        DialogResult = DialogResult.OK;
        Close();
    }

    private IReadOnlyList<string> GetStatuses()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => _workspace.TransferStatuses,
            WarehouseDocumentEditorMode.Inventory => _workspace.InventoryStatuses,
            _ => _workspace.WriteOffStatuses
        };
    }

    private string BuildTitle(OperationalWarehouseDocumentRecord? document)
    {
        var caption = BuildHeaderCaption();
        return document is null ? caption : $"{caption} {document.Number}";
    }

    private string BuildHeaderCaption()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Перемещение",
            WarehouseDocumentEditorMode.Inventory => "Инвентаризация",
            _ => "Списание"
        };
    }

    private string BuildSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Склад-источник, склад-получатель и позиции перемещения в одном окне.",
            WarehouseDocumentEditorMode.Inventory => "Фиксируйте склад и строки корректировки, чтобы провести инвентаризацию локально.",
            _ => "Причина списания и позиции движения сохраняются внутри desktop-контура."
        };
    }

    private string BuildLinesSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Позиции будут перенесены между складами после завершения документа.",
            WarehouseDocumentEditorMode.Inventory => "Используйте положительные и отрицательные корректировки по каждой позиции.",
            _ => "Позиции будут списаны со склада после проведения документа."
        };
    }

    private string BuildLineTitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Позиция перемещения",
            WarehouseDocumentEditorMode.Inventory => "Строка инвентаризации",
            _ => "Строка списания"
        };
    }

    private string BuildLineSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Выберите товар и укажите количество для перемещения между складами.",
            WarehouseDocumentEditorMode.Inventory => "Задайте товар и корректировку остатка на складе.",
            _ => "Выберите товар и укажите количество для списания."
        };
    }

    private static OperationalWarehouseDocumentRecord CreateDraft(
        OperationalWarehouseWorkspace workspace,
        WarehouseDocumentEditorMode mode)
    {
        return mode switch
        {
            WarehouseDocumentEditorMode.Transfer => workspace.CreateTransferDraft(),
            WarehouseDocumentEditorMode.Inventory => workspace.CreateInventoryDraft(),
            _ => workspace.CreateWriteOffDraft()
        };
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var index = comboBox.Items
                .Cast<object>()
                .Select((item, index) => (item: item.ToString() ?? string.Empty, index))
                .FirstOrDefault(pair => pair.item.Equals(value, StringComparison.OrdinalIgnoreCase))
                .index;
            if (index >= 0 && index < comboBox.Items.Count)
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private sealed class LineGridRow
    {
        public LineGridRow(OperationalWarehouseLineRecord source)
        {
            Source = source;
            LineId = source.Id;
            ItemCode = source.ItemCode;
            ItemName = source.ItemName;
            Quantity = source.Quantity;
            Unit = source.Unit;
            SourceLocation = source.SourceLocation;
            TargetLocation = source.TargetLocation;
        }

        [Browsable(false)]
        public OperationalWarehouseLineRecord Source { get; }

        [Browsable(false)]
        public Guid LineId { get; }

        public string ItemCode { get; }

        public string ItemName { get; }

        public decimal Quantity { get; }

        public string Unit { get; }

        public string SourceLocation { get; }

        public string TargetLocation { get; }
    }
}

public enum WarehouseDocumentEditorMode
{
    Transfer,
    Inventory,
    WriteOff
}
