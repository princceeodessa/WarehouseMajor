using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Model;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class ModelWorkspaceControl : UserControl
{
    private readonly ModelMapControl _mapControl = new();
    private readonly BindingSource _entityBindingSource = new();
    private readonly BindingSource _relationBindingSource = new();
    private readonly DataGridView _entityGrid;
    private readonly DataGridView _relationGrid;

    public ModelWorkspaceControl()
    {
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 244, 238);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(18, 16, 18, 18)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new Panel
        {
            Dock = DockStyle.Top,
            Height = 62,
            Padding = new Padding(0, 0, 0, 6)
        };
        header.Controls.Add(new Label
        {
            Text = "Служебный режим для команды разработки и внедрения.",
            Dock = DockStyle.Top,
            Height = 22,
            Font = new Font("Segoe UI", 9.5f),
            ForeColor = Color.FromArgb(114, 104, 93)
        });
        header.Controls.Add(new Label
        {
            Text = "Карта доменной модели и связей",
            Dock = DockStyle.Top,
            Height = 34,
            Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Color.FromArgb(40, 36, 31)
        });

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 420
        };

        var mapHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(247, 244, 238),
            BorderStyle = BorderStyle.FixedSingle
        };
        _mapControl.Dock = DockStyle.Fill;
        _mapControl.EntitySelected += (_, clrType) => SelectEntity(clrType);
        mapHost.Controls.Add(_mapControl);
        split.Panel1.Controls.Add(mapHost);

        var bottomSplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            SplitterDistance = 380
        };

        _entityGrid = DesktopGridFactory.CreateGrid(Array.Empty<ModelEntityRow>());
        _entityGrid.DataSource = _entityBindingSource;
        _entityGrid.SelectionChanged += (_, _) =>
        {
            if (_entityGrid.CurrentRow?.DataBoundItem is ModelEntityRow entityRow)
            {
                SelectEntity(entityRow.EntityType);
            }
        };

        _relationGrid = DesktopGridFactory.CreateGrid(Array.Empty<ModelRelationRow>());
        _relationGrid.DataSource = _relationBindingSource;

        bottomSplit.Panel1.Padding = new Padding(0, 14, 8, 0);
        bottomSplit.Panel2.Padding = new Padding(8, 14, 0, 0);
        bottomSplit.Panel1.Controls.Add(CreateGridShell("Сущности", "Все ядро новой системы.", _entityGrid));
        bottomSplit.Panel2.Controls.Add(CreateGridShell("Связи", "То, как модули сцеплены между собой.", _relationGrid));

        split.Panel2.Controls.Add(bottomSplit);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(split, 0, 1);
        Controls.Add(root);

        _entityBindingSource.DataSource = ModelCatalog.Entities
            .Select(entity => new ModelEntityRow(
                ModelCatalog.FindArea(entity.AreaKey)?.DisplayName ?? entity.AreaKey,
                entity.DisplayName,
                entity.ClrType.Name,
                entity.ClrType))
            .OrderBy(entity => entity.Area)
            .ThenBy(entity => entity.Entity)
            .ToArray();

        SelectEntity(ModelCatalog.Entities.FirstOrDefault()?.ClrType);
    }

    private void SelectEntity(Type? entityType)
    {
        _mapControl.SelectedEntityType = entityType;

        var relationRows = ModelCatalog.GetRelationshipsFor(entityType)
            .Select(relation => new ModelRelationRow(
                ModelCatalog.FindEntity(relation.SourceType)?.DisplayName ?? relation.SourceType.Name,
                ModelCatalog.FindEntity(relation.TargetType)?.DisplayName ?? relation.TargetType.Name,
                relation.Label,
                relation.Cardinality))
            .OrderBy(row => row.Source)
            .ThenBy(row => row.Target)
            .ToArray();

        _relationBindingSource.DataSource = relationRows;

        if (entityType is null)
        {
            return;
        }

        foreach (DataGridViewRow row in _entityGrid.Rows)
        {
            if (row.DataBoundItem is ModelEntityRow entityRow && entityRow.EntityType == entityType)
            {
                row.Selected = true;
                _entityGrid.CurrentCell = row.Cells[0];
                break;
            }
        }
    }

    private static Control CreateGridShell(string title, string subtitle, Control grid)
    {
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
            RowCount = 2
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

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

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(grid, 0, 1);
        panel.Controls.Add(root);
        return panel;
    }

    private sealed record ModelEntityRow(
        [property: DisplayName("Область")] string Area,
        [property: DisplayName("Сущность")] string Entity,
        [property: DisplayName("CLR тип")] string ClrType,
        Type EntityType);

    private sealed record ModelRelationRow(
        [property: DisplayName("Источник")] string Source,
        [property: DisplayName("Приемник")] string Target,
        [property: DisplayName("Связь")] string Relation,
        [property: DisplayName("Кратность")] string Cardinality);
}
