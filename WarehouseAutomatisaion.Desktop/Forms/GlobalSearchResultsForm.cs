using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Controls;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class GlobalSearchResultsForm : Form
{
    private readonly IReadOnlyList<DesktopBackplaneSearchHit> _results;
    private readonly BindingSource _bindingSource = new();
    private readonly DataGridView _grid = DesktopGridFactory.CreateGrid(Array.Empty<SearchGridRow>());
    private readonly TextBox _referenceTextBox = new();

    public GlobalSearchResultsForm(string query, IReadOnlyList<DesktopBackplaneSearchHit> results)
    {
        _results = results;
        SelectedModuleCode = null;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(980, 700);
        MinimumSize = new Size(980, 700);
        StartPosition = FormStartPosition.CenterParent;
        Text = $"Поиск: {query}";

        BuildLayout(query);
        LoadResults();
    }

    public string? SelectedModuleCode { get; private set; }

    private void BuildLayout(string query)
    {
        _grid.DataSource = _bindingSource;
        _grid.SelectionChanged += (_, _) => RefreshReference();
        _grid.DoubleClick += (_, _) => OpenSelectedModule();
        _referenceTextBox.Multiline = true;
        _referenceTextBox.ReadOnly = true;
        _referenceTextBox.ScrollBars = ScrollBars.Vertical;
        _referenceTextBox.BackColor = Color.FromArgb(255, 250, 241);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateHeader(query), 0, 0);
        root.Controls.Add(_grid, 0, 1);
        root.Controls.Add(_referenceTextBox, 0, 2);
        root.Controls.Add(CreateButtons(), 0, 3);
        Controls.Add(root);
    }

    private Control CreateHeader(string query)
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = $"Найдено результатов: {_results.Count:N0}. Двойной клик открывает нужный модуль.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = $"Глобальный поиск: {query}",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
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

        var openButton = CreateActionButton("Открыть модуль", Color.FromArgb(242, 194, 89), Color.FromArgb(42, 36, 29));
        openButton.Click += (_, _) => OpenSelectedModule();

        var closeButton = CreateActionButton("Закрыть", Color.White, Color.FromArgb(63, 55, 46));
        closeButton.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        closeButton.Click += (_, _) => DialogResult = DialogResult.Cancel;

        panel.Controls.Add(openButton);
        panel.Controls.Add(closeButton);
        AcceptButton = openButton;
        CancelButton = closeButton;
        return panel;
    }

    private static Button CreateActionButton(string text, Color backColor, Color foreColor)
    {
        var button = new Button
        {
            Text = text,
            Width = 152,
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

    private void LoadResults()
    {
        _bindingSource.DataSource = _results
            .Select(item => new SearchGridRow(
                item.ModuleCode,
                item.Scope,
                item.Title,
                item.Subtitle,
                item.Reference))
            .ToList();
        RefreshReference();
    }

    private void RefreshReference()
    {
        _referenceTextBox.Text = (_grid.CurrentRow?.DataBoundItem as SearchGridRow)?.Reference ?? string.Empty;
    }

    private void OpenSelectedModule()
    {
        var row = _grid.CurrentRow?.DataBoundItem as SearchGridRow;
        if (row is null)
        {
            return;
        }

        SelectedModuleCode = string.IsNullOrWhiteSpace(row.ModuleCode)
            ? row.Scope.Equals("audit", StringComparison.OrdinalIgnoreCase) ? "audit" : string.Empty
            : row.ModuleCode;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record SearchGridRow(
        string ModuleCode,
        string Scope,
        string Title,
        string Subtitle,
        string Reference);
}
