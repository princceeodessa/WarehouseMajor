using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class DashboardControl : UserControl
{
    public event EventHandler<string>? NavigationRequested;

    public DashboardControl(DemoWorkspace workspace)
    {
        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var topSection = CreateTopSection(workspace);

        var bottomSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 640,
            BackColor = DesktopTheme.Border
        };

        bottomSplit.Panel1.Padding = new Padding(0, 14, 8, 0);
        bottomSplit.Panel2.Padding = new Padding(8, 14, 0, 0);
        bottomSplit.Panel1.Controls.Add(CreateGridPanel("Сигналы на сегодня", "То, что требует внимания прямо сейчас.", workspace.Dashboard.Alerts));
        bottomSplit.Panel2.Controls.Add(CreateGridPanel(
            "Очередь работы",
            "Кто и что должен закрыть в течение смены.",
            workspace.Dashboard.WorkQueue,
            useCompactColumns: true,
            allowExpandDialog: true));

        root.Controls.Add(topSection, 0, 0);
        root.Controls.Add(bottomSplit, 0, 1);
        Controls.Add(root);
    }

    private Control CreateTopSection(DemoWorkspace workspace)
    {
        var host = new Panel
        {
            Dock = DockStyle.Top,
            Height = 180,
            AutoScroll = true,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        var cardsFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 166,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 10, 0)
        };

        foreach (var metric in workspace.Dashboard.Metrics.Take(4))
        {
            cardsFlow.Controls.Add(CreateMetricCard(metric));
        }

        cardsFlow.Controls.Add(CreateQuickActionsCard(workspace.Dashboard.QuickActions));

        host.Controls.Add(cardsFlow);
        return host;
    }

    private Control CreateQuickActionsCard(IReadOnlyList<QuickAction> actions)
    {
        var panel = CreateCardShell();
        panel.Dock = DockStyle.None;
        panel.Width = 332;
        panel.Padding = new Padding(18, 16, 18, 16);

        panel.Controls.Add(new Label
        {
            Text = "Быстрые переходы",
            Dock = DockStyle.Top,
            Height = 30,
            Font = DesktopTheme.TitleFont(12f),
            ForeColor = DesktopTheme.TextPrimary
        });

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            Margin = new Padding(0),
            Padding = new Padding(0, 4, 0, 0)
        };

        var visibleActions = actions
            .Where(item => !string.IsNullOrWhiteSpace(item.Caption))
            .Take(3)
            .ToArray();

        foreach (var action in visibleActions)
        {
            var button = new Button
            {
                Text = action.Caption,
                Width = 264,
                Height = 38,
                FlatStyle = FlatStyle.Flat,
                Font = DesktopTheme.EmphasisFont(9.4f),
                Cursor = Cursors.Hand,
                Margin = new Padding(0, 0, 0, 6),
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(14, 0, 14, 0)
            };
            DesktopSurfaceFactory.ApplyTone(button, DesktopButtonTone.Secondary);
            button.Click += (_, _) => NavigationRequested?.Invoke(this, action.TargetKey);

            flow.Controls.Add(button);
            if (!string.IsNullOrWhiteSpace(action.Hint))
            {
                var hint = new Label
                {
                    Text = action.Hint,
                    AutoSize = false,
                    Width = 264,
                    Height = 26,
                    Margin = new Padding(8, -2, 0, 8),
                    Font = DesktopTheme.BodyFont(8.6f),
                    ForeColor = DesktopTheme.TextMuted
                };
                flow.Controls.Add(hint);
            }
        }

        if (visibleActions.Length == 0)
        {
            flow.Controls.Add(new Label
            {
                Text = "Нет быстрых команд для текущего контура.",
                AutoSize = false,
                Width = 264,
                Height = 26,
                Margin = new Padding(0, 2, 0, 0),
                Font = DesktopTheme.BodyFont(9f),
                ForeColor = DesktopTheme.TextMuted
            });
        }
        else if (actions.Count > visibleActions.Length)
        {
            flow.Controls.Add(new Label
            {
                Text = $"+{actions.Count - visibleActions.Length} еще в разделах",
                AutoSize = false,
                Width = 264,
                Height = 22,
                Margin = new Padding(0, -2, 0, 0),
                Font = DesktopTheme.BodyFont(8.4f),
                ForeColor = DesktopTheme.TextMuted
            });
        }

        panel.Controls.Add(flow);
        return panel;
    }

    private static Control CreateMetricCard(MetricCard metric)
    {
        var panel = CreateCardShell();
        panel.Dock = DockStyle.None;
        panel.Width = 252;
        panel.Padding = new Padding(14, 12, 14, 12);

        var accentBar = new Panel
        {
            Dock = DockStyle.Left,
            Width = 6,
            BackColor = metric.AccentColor
        };
        panel.Controls.Add(accentBar);

        var inner = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14, 0, 0, 0)
        };
        inner.Controls.Add(new Label
        {
            Text = metric.Hint,
            Dock = DockStyle.Top,
            Height = 34,
            Font = DesktopTheme.BodyFont(8.8f),
            ForeColor = DesktopTheme.TextSecondary
        });
        inner.Controls.Add(new Label
        {
            Text = metric.Value,
            Dock = DockStyle.Top,
            Height = 38,
            Font = DesktopTheme.TitleFont(20f),
            ForeColor = DesktopTheme.TextPrimary
        });
        inner.Controls.Add(new Label
        {
            Text = metric.Title,
            Dock = DockStyle.Top,
            Height = 24,
            Font = DesktopTheme.EmphasisFont(10f),
            ForeColor = DesktopTheme.TextPrimary
        });

        panel.Controls.Add(inner);
        return panel;
    }

    private Control CreateGridPanel(
        string title,
        string subtitle,
        object dataSource,
        bool useCompactColumns = false,
        bool allowExpandDialog = false)
    {
        var panel = CreateCardShell();
        panel.Padding = new Padding(16);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = allowExpandDialog ? 3 : 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        if (allowExpandDialog)
        {
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        }

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 52
        };
        header.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 22,
            Font = DesktopTheme.BodyFont(9f),
            ForeColor = DesktopTheme.TextSecondary
        });
        header.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 28,
            Font = DesktopTheme.TitleFont(12f),
            ForeColor = DesktopTheme.TextPrimary
        });

        var grid = DesktopGridFactory.CreateGrid(dataSource);
        if (useCompactColumns)
        {
            ConfigureCompactGrid(grid);
            RebindGrid(grid);
        }

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(grid, 0, 1);
        if (allowExpandDialog)
        {
            var footer = new Panel
            {
                Dock = DockStyle.Fill,
                Height = 34
            };
            var expandButton = DesktopSurfaceFactory.CreateActionButton(
                "Показать всё",
                (_, _) => ShowGridDialog(title, subtitle, dataSource),
                DesktopButtonTone.Ghost,
                new Padding(0));
            expandButton.Text = "Show all";
            expandButton.AutoSize = false;
            expandButton.Width = 126;
            expandButton.Height = 30;
            expandButton.Font = DesktopTheme.EmphasisFont(8.8f);
            expandButton.Dock = DockStyle.Right;
            var hint = new Label
            {
                Text = "Полный список",
                Dock = DockStyle.Right,
                Width = 104,
                TextAlign = ContentAlignment.MiddleRight,
                Font = DesktopTheme.BodyFont(8.6f),
                ForeColor = DesktopTheme.TextMuted
            };
            hint.Visible = false;
            footer.Controls.Add(expandButton);
            footer.Controls.Add(hint);
            root.Controls.Add(footer, 0, 2);
        }

        panel.Controls.Add(root);
        return panel;
    }

    private static void RebindGrid(DataGridView grid)
    {
        var source = grid.DataSource;
        if (source is null)
        {
            return;
        }

        grid.DataSource = null;
        grid.DataSource = source;
    }

    private static void ConfigureCompactGrid(DataGridView grid)
    {
        grid.ScrollBars = ScrollBars.Both;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.ColumnHeadersDefaultCellStyle.Font = DesktopTheme.EmphasisFont(8.6f);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
        grid.DefaultCellStyle.Font = DesktopTheme.BodyFont(8.8f);
        grid.DefaultCellStyle.Padding = new Padding(2, 0, 2, 0);
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        grid.RowTemplate.Height = 30;

        grid.DataBindingComplete += (_, _) =>
        {
            var visibleColumns = grid.Columns
                .Cast<DataGridViewColumn>()
                .Where(column => column.Visible)
                .ToList();

            var priority = FindCompactColumn(visibleColumns, "приоритет", "priority");
            var module = FindCompactColumn(visibleColumns, "модуль", "module");
            var task = FindCompactColumn(visibleColumns, "задача", "task");

            var keepSet = new HashSet<DataGridViewColumn>();
            if (priority is not null)
            {
                keepSet.Add(priority);
            }

            if (module is not null)
            {
                keepSet.Add(module);
            }

            if (task is not null)
            {
                keepSet.Add(task);
            }

            if (keepSet.Count >= 2)
            {
                foreach (var column in visibleColumns)
                {
                    column.Visible = keepSet.Contains(column);
                }
            }
            else
            {
                foreach (var column in visibleColumns.Skip(2))
                {
                    column.Visible = false;
                }
            }

            foreach (DataGridViewColumn column in grid.Columns)
            {
                if (!column.Visible)
                {
                    continue;
                }

                var key = $"{column.DataPropertyName} {column.HeaderText}";
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
                if (ContainsAny(key, "модуль", "module"))
                {
                    column.HeaderText = "Модуль";
                    column.MinimumWidth = 68;
                    column.FillWeight = 92;
                    continue;
                }

                if (ContainsAny(key, "задача", "task"))
                {
                    column.HeaderText = "Задача";
                    column.MinimumWidth = 130;
                    column.FillWeight = 196;
                    continue;
                }

                if (ContainsAny(key, "приоритет", "priority"))
                {
                    column.HeaderText = "Приор.";
                    column.MinimumWidth = 78;
                    column.FillWeight = 70;
                    continue;
                }

                column.MinimumWidth = 90;
                column.FillWeight = 120;
            }
        };
    }

    private static DataGridViewColumn? FindCompactColumn(IEnumerable<DataGridViewColumn> columns, params string[] patterns)
    {
        return columns.FirstOrDefault(column =>
        {
            var key = $"{column.DataPropertyName} {column.HeaderText} {column.Name}";
            return ContainsAny(key, patterns);
        });
    }

    private static bool ContainsAny(string source, params string[] patterns)
    {
        return patterns.Any(pattern => source.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowGridDialog(string title, string subtitle, object dataSource)
    {
        using var dialog = new Form
        {
            Text = $"{title} - полный список",
            StartPosition = FormStartPosition.CenterParent,
            MinimumSize = new Size(920, 600),
            Size = new Size(1080, 700),
            BackColor = DesktopTheme.AppBackground
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var titlePanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 58
        };
        titlePanel.Controls.Add(new Label
        {
            Text = subtitle,
            Dock = DockStyle.Top,
            Height = 24,
            Font = DesktopTheme.BodyFont(9.2f),
            ForeColor = DesktopTheme.TextSecondary
        });
        titlePanel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 32,
            Font = DesktopTheme.TitleFont(13f),
            ForeColor = DesktopTheme.TextPrimary
        });

        var gridShell = DesktopSurfaceFactory.CreateCardShell();
        gridShell.Padding = new Padding(10);
        var grid = DesktopGridFactory.CreateGrid(dataSource);
        gridShell.Controls.Add(grid);

        root.Controls.Add(titlePanel, 0, 0);
        root.Controls.Add(gridShell, 0, 1);
        dialog.Controls.Add(root);
        DialogTabsHost.ShowDialog(dialog, FindForm());
    }

    private static Panel CreateCardShell()
    {
        var panel = DesktopSurfaceFactory.CreateCardShell();
        panel.Height = 170;
        panel.Margin = new Padding(0, 0, 16, 0);
        return panel;
    }
}
