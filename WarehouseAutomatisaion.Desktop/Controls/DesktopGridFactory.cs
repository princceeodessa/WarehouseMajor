namespace WarehouseAutomatisaion.Desktop.Controls;

public static class DesktopGridFactory
{
    public static DataGridView CreateGrid(object dataSource)
    {
        var grid = new OptimizedDataGridView
        {
            Dock = DockStyle.Fill,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            ReadOnly = true,
            MultiSelect = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
            AutoGenerateColumns = true,
            BorderStyle = BorderStyle.None,
            BackgroundColor = DesktopTheme.Surface,
            GridColor = DesktopTheme.Border,
            EnableHeadersVisualStyles = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ClipboardCopyMode = DataGridViewClipboardCopyMode.EnableWithoutHeaderText
        };

        grid.RowTemplate.Height = 34;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeight = 38;
        grid.ColumnHeadersDefaultCellStyle.BackColor = DesktopTheme.SurfaceMuted;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = DesktopTheme.TextSecondary;
        grid.ColumnHeadersDefaultCellStyle.Font = DesktopTheme.EmphasisFont(9.2f);
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.DefaultCellStyle.BackColor = DesktopTheme.Surface;
        grid.DefaultCellStyle.ForeColor = DesktopTheme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = DesktopTheme.GridSelectionBackground;
        grid.DefaultCellStyle.SelectionForeColor = DesktopTheme.GridSelectionText;
        grid.DefaultCellStyle.Font = DesktopTheme.BodyFont(9.2f);
        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = DesktopTheme.SurfaceAlt;

        grid.DataBindingComplete += (_, _) => ConfigureColumns(grid);
        grid.DataSource = dataSource;
        ConfigureColumns(grid);

        return grid;
    }

    private static void ConfigureColumns(DataGridView grid)
    {
        if (grid.Columns.Count == 0)
        {
            return;
        }

        var rowCount = grid.Rows.Count;
        var compactMode = rowCount > 90 || grid.Columns.Count > 8;

        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (IsTechnicalIdColumn(column))
            {
                column.Visible = false;
                continue;
            }

            column.SortMode = DataGridViewColumnSortMode.Automatic;
            column.MinimumWidth = 80;
            ApplyAlignment(column);

            if (compactMode)
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
                column.Width = ResolveCompactColumnWidth(column);
            }
            else
            {
                column.AutoSizeMode = DataGridViewAutoSizeColumnMode.DisplayedCellsExceptHeader;
            }
        }
    }

    private static bool IsTechnicalIdColumn(DataGridViewColumn column)
    {
        return string.Equals(column.DataPropertyName, "Id", StringComparison.OrdinalIgnoreCase)
               || string.Equals(column.Name, "Id", StringComparison.OrdinalIgnoreCase)
               || string.Equals(column.HeaderText, "Id", StringComparison.OrdinalIgnoreCase);
    }

    private static void ApplyAlignment(DataGridViewColumn column)
    {
        var key = $"{column.DataPropertyName} {column.HeaderText}";
        if (ContainsAny(key, "date", "time", "дата", "время", "срок"))
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return;
        }

        if (ContainsAny(key, "price", "amount", "sum", "count", "percent", "qty", "колич", "цена", "сумм", "процент"))
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            return;
        }

        if (ContainsAny(key, "status", "state", "result", "статус", "результат"))
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            return;
        }

        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
    }

    private static int ResolveCompactColumnWidth(DataGridViewColumn column)
    {
        var key = $"{column.DataPropertyName} {column.HeaderText}";
        if (ContainsAny(key, "name", "title", "comment", "message", "reference", "summary", "наименование", "номенклатура", "клиент", "поставщик", "комментар", "сообщение"))
        {
            return 280;
        }

        if (ContainsAny(key, "status", "state", "result", "статус", "результат", "warehouse", "склад", "supplier", "customer", "actor"))
        {
            return 150;
        }

        if (ContainsAny(key, "date", "logged", "дата", "время"))
        {
            return 130;
        }

        if (ContainsAny(key, "price", "amount", "sum", "count", "percent", "колич", "цена", "сумм", "процент"))
        {
            return 120;
        }

        if (ContainsAny(key, "code", "number", "id", "код", "номер"))
        {
            return 120;
        }

        return 140;
    }

    private static bool ContainsAny(string source, params string[] patterns)
    {
        return patterns.Any(pattern => source.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }
}

internal sealed class OptimizedDataGridView : DataGridView
{
    public OptimizedDataGridView()
    {
        DoubleBuffered = true;
    }
}
