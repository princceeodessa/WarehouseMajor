using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class FunctionalCoverageControl : UserControl
{
    public FunctionalCoverageControl(FunctionalCoverageSnapshot snapshot)
    {
        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateSummaryCards(snapshot.SummaryCards), 0, 1);
        root.Controls.Add(CreateModulesTabs(snapshot.Modules), 0, 2);
        root.Controls.Add(CreateBottomNote(), 0, 3);

        Controls.Add(root);
    }

    private static Control CreateHeader()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 68,
            Padding = new Padding(0, 0, 0, 8)
        };

        panel.Controls.Add(new Label
        {
            Text = "Здесь зафиксирован основной прикладной контур, который должен полностью заменить 1С для сотрудников.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });

        panel.Controls.Add(new Label
        {
            Text = "Контур замены 1С",
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        return panel;
    }

    private static Control CreateSummaryCards(IReadOnlyList<CoverageSummaryCard> cards)
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

        foreach (var card in cards)
        {
            flow.Controls.Add(CreateSummaryCard(card));
        }

        return flow;
    }

    private static Control CreateSummaryCard(CoverageSummaryCard card)
    {
        var panel = new Panel
        {
            Width = 252,
            Height = 100,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Margin = new Padding(0, 0, 12, 12),
            Padding = new Padding(14, 12, 14, 12)
        };

        var accent = new Panel
        {
            Dock = DockStyle.Left,
            Width = 5,
            BackColor = card.AccentColor
        };

        var valueLabel = new Label
        {
            Text = card.Value,
            Dock = DockStyle.Top,
            Height = 40,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(43, 39, 34)
        };

        var hintLabel = new Label
        {
            Text = card.Hint,
            Dock = DockStyle.Top,
            Height = 36,
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(112, 103, 92)
        };

        var titleLabel = new Label
        {
            Text = card.Title,
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
            ForeColor = Color.FromArgb(68, 61, 53)
        };

        var content = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 0, 0)
        };
        content.Controls.Add(hintLabel);
        content.Controls.Add(valueLabel);
        content.Controls.Add(titleLabel);

        panel.Controls.Add(content);
        panel.Controls.Add(accent);
        return panel;
    }

    private static Control CreateModulesTabs(IReadOnlyList<FunctionalModuleDefinition> modules)
    {
        var tabs = DesktopSurfaceFactory.CreateTabControl();

        foreach (var module in modules)
        {
            var tab = new TabPage(module.Title)
            {
                Padding = new Padding(12)
            };

            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            root.Controls.Add(new Label
            {
                Text = module.Goal,
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = new Font("Segoe UI", 9.5f),
                ForeColor = Color.FromArgb(107, 98, 88),
                Padding = new Padding(0, 0, 0, 8)
            }, 0, 0);

            root.Controls.Add(DesktopGridFactory.CreateGrid(module.Scenarios), 0, 1);
            tab.Controls.Add(root);
            tabs.TabPages.Add(tab);
        }

        return tabs;
    }

    private static Control CreateBottomNote()
    {
        return new Label
        {
            Dock = DockStyle.Top,
            Height = 44,
            Padding = new Padding(4, 10, 0, 0),
            Text = "Правило переноса: сначала закрываем критические цепочки документов и рабочие места сотрудников, затем добираем возвраты, печать, аудит и сервисные функции.",
            Font = new Font("Segoe UI", 9f),
            ForeColor = Color.FromArgb(109, 100, 90)
        };
    }
}

