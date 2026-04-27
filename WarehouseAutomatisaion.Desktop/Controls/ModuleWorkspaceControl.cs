namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed record ModuleActionDefinition(string Caption, string Hint);

public sealed record ModuleTabDefinition(string Title, string Summary, object DataSource);

public sealed class ModuleWorkspaceControl : UserControl
{
    public ModuleWorkspaceControl(
        string title,
        string subtitle,
        IReadOnlyList<ModuleActionDefinition> actions,
        IReadOnlyList<ModuleTabDefinition> tabs)
    {
        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 64,
            Padding = new Padding(0, 0, 0, 6)
        };
        headerPanel.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 22,
            Font = DesktopTheme.BodyFont(9.5f),
            ForeColor = DesktopTheme.TextSecondary
        });
        headerPanel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 34,
            Font = DesktopTheme.TitleFont(18f),
            ForeColor = DesktopTheme.TextPrimary
        });

        var actionsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0, 0, 0, 8)
        };

        foreach (var action in actions)
        {
            var caption = action.Caption;
            var hint = action.Hint;
            var button = DesktopSurfaceFactory.CreateActionButton(
                caption,
                (_, _) => MessageBox.Show(
                    $"Сценарий: {caption}{Environment.NewLine}{Environment.NewLine}{hint}{Environment.NewLine}{Environment.NewLine}Форма ввода будет подключена следующим шагом.",
                    "Команда",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information),
                DesktopButtonTone.Secondary,
                new Padding(0, 0, 10, 0));

            actionsPanel.Controls.Add(button);
        }

        var tabsControl = DesktopSurfaceFactory.CreateTabControl();

        foreach (var tab in tabs)
        {
            var tabPage = new TabPage(tab.Title)
            {
                Padding = new Padding(10)
            };

            var tabRoot = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2
            };
            tabRoot.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            tabRoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            tabRoot.Controls.Add(new Label
            {
                Text = tab.Summary,
                Dock = DockStyle.Top,
                AutoSize = true,
                Font = DesktopTheme.BodyFont(9.5f),
                ForeColor = DesktopTheme.TextSecondary,
                Padding = new Padding(0, 0, 0, 8)
            }, 0, 0);

            tabRoot.Controls.Add(DesktopGridFactory.CreateGrid(tab.DataSource), 0, 1);

            tabPage.Controls.Add(tabRoot);
            tabsControl.TabPages.Add(tabPage);
        }

        root.Controls.Add(headerPanel, 0, 0);
        root.Controls.Add(actionsPanel, 0, 1);
        root.Controls.Add(tabsControl, 0, 2);
        Controls.Add(root);
    }
}
