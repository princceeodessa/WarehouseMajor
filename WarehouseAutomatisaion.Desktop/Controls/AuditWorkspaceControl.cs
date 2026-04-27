using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class AuditWorkspaceControl : UserControl
{
    private readonly SalesWorkspace _salesWorkspace;
    private readonly BindingSource _entriesBindingSource = new();
    private readonly TextBox _searchTextBox = new();
    private readonly Label _noteLabel = new();
    private readonly Label _salesCountLabel = new();
    private readonly Label _purchasingCountLabel = new();
    private readonly Label _warehouseCountLabel = new();
    private readonly Label _totalCountLabel = new();
    private readonly DataGridView _grid = DesktopGridFactory.CreateGrid(Array.Empty<AuditGridRow>());
    private AuditWorkspaceSnapshot _snapshot;

    public AuditWorkspaceControl(SalesWorkspace salesWorkspace, AuditWorkspaceSnapshot? snapshot = null)
    {
        _salesWorkspace = salesWorkspace;
        _snapshot = snapshot ?? AuditWorkspaceSnapshot.Create(salesWorkspace);

        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 244, 238);

        BuildLayout();
        RefreshView();

        _salesWorkspace.Changed += HandleWorkspaceChanged;
        Disposed += (_, _) => _salesWorkspace.Changed -= HandleWorkspaceChanged;
    }

    private void HandleWorkspaceChanged(object? sender, EventArgs e)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => ReloadSnapshot());
            return;
        }

        ReloadSnapshot();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateNote(), 0, 1);
        root.Controls.Add(CreateSummaryCards(), 0, 2);
        root.Controls.Add(CreateAuditGridShell(), 0, 3);

        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var panel = new Panel { Dock = DockStyle.Top, Height = 66, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label
        {
            Text = "Общий журнал действий по продажам, закупкам и складу без возврата в 1С.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        panel.Controls.Add(new Label
        {
            Text = "Единый аудит",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });
        return panel;
    }

    private Control CreateNote()
    {
        _noteLabel.Dock = DockStyle.Top;
        _noteLabel.Height = 42;
        _noteLabel.Font = new Font("Segoe UI", 9.2f);
        _noteLabel.ForeColor = Color.FromArgb(97, 88, 80);

        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 54,
            Padding = new Padding(12, 10, 12, 0),
            BackColor = Color.FromArgb(255, 250, 241)
        };
        panel.Controls.Add(_noteLabel);
        return panel;
    }

    private Control CreateSummaryCards()
    {
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Padding = new Padding(0, 0, 0, 12)
        };
        flow.Controls.Add(CreateSummaryCard("Продажи", "Операции менеджеров и документов продаж.", _salesCountLabel, Color.FromArgb(78, 160, 190)));
        flow.Controls.Add(CreateSummaryCard("Закупки", "Согласование, счета и приемка.", _purchasingCountLabel, Color.FromArgb(201, 134, 64)));
        flow.Controls.Add(CreateSummaryCard("Склад", "Перемещения, инвентаризации и списания.", _warehouseCountLabel, Color.FromArgb(196, 92, 83)));
        flow.Controls.Add(CreateSummaryCard("Всего", "Все записи локального рабочего контура.", _totalCountLabel, Color.FromArgb(79, 174, 92)));
        return flow;
    }

    private static Control CreateSummaryCard(string title, string hint, Label valueLabel, Color accentColor)
    {
        valueLabel.Dock = DockStyle.Top;
        valueLabel.Height = 36;
        valueLabel.Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
        valueLabel.ForeColor = Color.FromArgb(43, 39, 34);

        var panel = new Panel
        {
            Width = 244,
            Height = 96,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 12, 12),
            Padding = new Padding(14, 12, 14, 12)
        };
        var accent = new Panel { Dock = DockStyle.Left, Width = 5, BackColor = accentColor };
        var content = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        content.Controls.Add(new Label { Text = hint, Dock = DockStyle.Top, Height = 34, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(112, 103, 92) });
        content.Controls.Add(valueLabel);
        content.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold), ForeColor = Color.FromArgb(68, 61, 53) });
        panel.Controls.Add(content);
        panel.Controls.Add(accent);
        return panel;
    }

    private Control CreateAuditGridShell()
    {
        _searchTextBox.Width = 280;
        _searchTextBox.Font = new Font("Segoe UI", 10f);
        _searchTextBox.PlaceholderText = "Поиск по модулю, пользователю, сущности и действию";
        _searchTextBox.TextChanged += (_, _) => RefreshGrid();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 10)
        };
        toolbar.Controls.Add(_searchTextBox);
        toolbar.Controls.Add(CreateActionButton("Обновить", (_, _) => ReloadSnapshot()));

        _grid.DataSource = _entriesBindingSource;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateSectionHeader("Журнал операций", "Показывает последние действия по всем автономным модулям desktop-контура."), 0, 0);
        root.Controls.Add(_grid, 0, 1);

        var wrapper = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        wrapper.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrapper.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        wrapper.Controls.Add(toolbar, 0, 0);
        wrapper.Controls.Add(root, 0, 1);
        return wrapper;
    }

    private static Control CreateSectionHeader(string title, string subtitle)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
            ForeColor = Color.FromArgb(47, 42, 36)
        });
        return header;
    }

    private static Button CreateActionButton(string text, EventHandler handler)
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

    private void ReloadSnapshot()
    {
        _snapshot = AuditWorkspaceSnapshot.Create(_salesWorkspace);
        RefreshView();
    }

    private void RefreshView()
    {
        SuspendLayout();
        try
        {
        _noteLabel.Text = $"Оператор: {(_salesWorkspace.CurrentOperator.Length == 0 ? Environment.UserName : _salesWorkspace.CurrentOperator)}. Журнал собирается из общей MySQL-базы, а при недоступности БД переключается на локальный fallback.";
        _salesCountLabel.Text = _snapshot.SalesCount.ToString("N0");
        _purchasingCountLabel.Text = _snapshot.PurchasingCount.ToString("N0");
        _warehouseCountLabel.Text = _snapshot.WarehouseCount.ToString("N0");
        _totalCountLabel.Text = _snapshot.TotalCount.ToString("N0");
        RefreshGrid();
        }
        finally
        {
            ResumeLayout(true);
        }
    }

    private void RefreshGrid()
    {
        var search = _searchTextBox.Text.Trim();
        _entriesBindingSource.DataSource = _snapshot.Entries
            .Where(entry => string.IsNullOrWhiteSpace(search)
                || entry.Module.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Actor.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.EntityType.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.EntityNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Action.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Result.Contains(search, StringComparison.OrdinalIgnoreCase)
                || entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new AuditGridRow(
                entry.LoggedAt,
                entry.Module,
                entry.Actor,
                entry.EntityType,
                entry.EntityNumber,
                entry.Action,
                entry.Result,
                entry.Message))
            .ToArray();
    }

    private sealed record AuditGridRow(
        [property: DisplayName("Время")] DateTime LoggedAt,
        [property: DisplayName("Модуль")] string Module,
        [property: DisplayName("Пользователь")] string Actor,
        [property: DisplayName("Сущность")] string EntityType,
        [property: DisplayName("Номер")] string EntityNumber,
        [property: DisplayName("Действие")] string Action,
        [property: DisplayName("Результат")] string Result,
        [property: DisplayName("Сообщение")] string Message);
}

