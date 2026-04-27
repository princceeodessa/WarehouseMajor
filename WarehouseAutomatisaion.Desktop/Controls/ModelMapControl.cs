using System.Drawing.Drawing2D;
using WarehouseAutomatisaion.Desktop.Model;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class ModelMapControl : Control
{
    private readonly Dictionary<Type, RectangleF> _nodeBounds = [];
    private readonly Dictionary<string, RectangleF> _areaBounds = [];
    private Type? _selectedEntityType;

    public event EventHandler<Type?>? EntitySelected;

    public Type? SelectedEntityType
    {
        get => _selectedEntityType;
        set
        {
            if (_selectedEntityType == value)
            {
                return;
            }

            _selectedEntityType = value;
            Invalidate();
        }
    }

    public ModelMapControl()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(247, 244, 238);
        MinimumSize = new Size(1480, 760);
        ResizeRedraw = true;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.Clear(BackColor);

        BuildLayout();
        DrawRelationships(e.Graphics);
        DrawAreas(e.Graphics);
        DrawNodes(e.Graphics);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        foreach (var pair in _nodeBounds)
        {
            if (pair.Value.Contains(e.Location))
            {
                SelectedEntityType = pair.Key;
                EntitySelected?.Invoke(this, pair.Key);
                return;
            }
        }
    }

    private void BuildLayout()
    {
        _nodeBounds.Clear();
        _areaBounds.Clear();

        const float outerPadding = 20f;
        const float areaGap = 18f;
        const float areaWidth = 220f;
        const float headerHeight = 44f;
        const float nodeHeight = 34f;
        const float nodeGap = 10f;
        const float innerPadding = 14f;

        foreach (var area in ModelCatalog.Areas)
        {
            var areaEntities = ModelCatalog.Entities
                .Where(entity => entity.AreaKey == area.Key)
                .OrderBy(entity => entity.DisplayName)
                .ToArray();

            var x = outerPadding + area.Order * (areaWidth + areaGap);
            var y = outerPadding;
            var contentHeight = headerHeight + innerPadding + areaEntities.Length * (nodeHeight + nodeGap) + innerPadding - nodeGap;
            var height = Math.Max(180f, contentHeight);

            var areaRect = new RectangleF(x, y, areaWidth, height);
            _areaBounds[area.Key] = areaRect;

            var nodeY = y + headerHeight + innerPadding;
            foreach (var entity in areaEntities)
            {
                var nodeRect = new RectangleF(x + innerPadding, nodeY, areaWidth - innerPadding * 2, nodeHeight);
                _nodeBounds[entity.ClrType] = nodeRect;
                nodeY += nodeHeight + nodeGap;
            }
        }
    }

    private void DrawAreas(Graphics graphics)
    {
        using var titleFont = new Font("Segoe UI Semibold", 10.5f);
        using var subtitleFont = new Font("Segoe UI", 8.5f);

        foreach (var area in ModelCatalog.Areas)
        {
            var bounds = _areaBounds[area.Key];
            using var backgroundBrush = new SolidBrush(Color.FromArgb(250, 250, 247));
            using var borderPen = new Pen(Color.FromArgb(226, 221, 212));
            using var headerBrush = new SolidBrush(area.AccentColor);
            using var titleBrush = new SolidBrush(Color.White);
            using var subtitleBrush = new SolidBrush(Color.FromArgb(112, 98, 84));

            FillRoundedRectangle(graphics, backgroundBrush, bounds, 18f);
            DrawRoundedRectangle(graphics, borderPen, bounds, 18f);

            var headerRect = new RectangleF(bounds.X, bounds.Y, bounds.Width, 40f);
            FillRoundedTopRectangle(graphics, headerBrush, headerRect, 18f);

            graphics.DrawString(area.DisplayName, titleFont, titleBrush, new PointF(bounds.X + 12f, bounds.Y + 10f));

            var entityCount = ModelCatalog.Entities.Count(entity => entity.AreaKey == area.Key);
            graphics.DrawString($"{entityCount} entities", subtitleFont, subtitleBrush, new PointF(bounds.X + 12f, bounds.Bottom - 24f));
        }
    }

    private void DrawNodes(Graphics graphics)
    {
        using var nodeFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        using var selectedFont = new Font("Segoe UI Semibold", 9.5f);

        foreach (var entity in ModelCatalog.Entities)
        {
            if (!_nodeBounds.TryGetValue(entity.ClrType, out var rect))
            {
                continue;
            }

            var area = ModelCatalog.FindArea(entity.AreaKey)!;
            var isSelected = entity.ClrType == SelectedEntityType;
            var isRelated = SelectedEntityType is not null &&
                            ModelCatalog.GetRelationshipsFor(SelectedEntityType)
                                .Any(relationship => relationship.SourceType == entity.ClrType || relationship.TargetType == entity.ClrType);

            var fillColor = isSelected
                ? area.AccentColor
                : isRelated
                    ? Color.FromArgb(236, 232, 255)
                    : Color.White;

            var borderColor = isSelected
                ? area.AccentColor
                : isRelated
                    ? Color.FromArgb(158, 142, 212)
                    : Color.FromArgb(215, 210, 201);

            using var fillBrush = new SolidBrush(fillColor);
            using var borderPen = new Pen(borderColor, isSelected ? 2f : 1f);

            FillRoundedRectangle(graphics, fillBrush, rect, 12f);
            DrawRoundedRectangle(graphics, borderPen, rect, 12f);

            var textRect = RectangleF.Inflate(rect, -10f, -7f);
            graphics.DrawString(
                entity.DisplayName,
                isSelected ? selectedFont : nodeFont,
                isSelected ? Brushes.White : Brushes.Black,
                textRect,
                new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap,
                    LineAlignment = StringAlignment.Center
                });
        }
    }

    private void DrawRelationships(Graphics graphics)
    {
        using var normalPen = new Pen(Color.FromArgb(192, 187, 180), 1.4f);
        using var relatedPen = new Pen(Color.FromArgb(109, 91, 188), 2.2f);
        using var selectedPen = new Pen(Color.FromArgb(46, 139, 87), 2.6f);
        using var arrowCap = new AdjustableArrowCap(4, 4, true);
        normalPen.CustomEndCap = arrowCap;
        relatedPen.CustomEndCap = arrowCap;
        selectedPen.CustomEndCap = arrowCap;

        var selectedRelationships = ModelCatalog.GetRelationshipsFor(SelectedEntityType);

        foreach (var relationship in ModelCatalog.Relationships)
        {
            if (!_nodeBounds.TryGetValue(relationship.SourceType, out var sourceRect) ||
                !_nodeBounds.TryGetValue(relationship.TargetType, out var targetRect))
            {
                continue;
            }

            var sourcePoint = new PointF(sourceRect.Right, sourceRect.Top + sourceRect.Height / 2f);
            var targetPoint = new PointF(targetRect.Left, targetRect.Top + targetRect.Height / 2f);
            var horizontalOffset = Math.Abs(targetPoint.X - sourcePoint.X) * 0.45f;

            using var path = new GraphicsPath();
            path.AddBezier(
                sourcePoint,
                new PointF(sourcePoint.X + horizontalOffset, sourcePoint.Y),
                new PointF(targetPoint.X - horizontalOffset, targetPoint.Y),
                targetPoint);

            var involvesSelected = SelectedEntityType is not null &&
                                   (relationship.SourceType == SelectedEntityType || relationship.TargetType == SelectedEntityType);

            var relatesToSelection = SelectedEntityType is not null &&
                                     selectedRelationships.Any(link =>
                                         link.SourceType == relationship.SourceType &&
                                         link.TargetType == relationship.TargetType &&
                                         link.Label == relationship.Label);

            var pen = involvesSelected ? selectedPen : relatesToSelection ? relatedPen : normalPen;
            graphics.DrawPath(pen, path);
        }
    }

    private static void FillRoundedRectangle(Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = CreateRoundedRectanglePath(rect, radius);
        graphics.FillPath(brush, path);
    }

    private static void DrawRoundedRectangle(Graphics graphics, Pen pen, RectangleF rect, float radius)
    {
        using var path = CreateRoundedRectanglePath(rect, radius);
        graphics.DrawPath(pen, path);
    }

    private static void FillRoundedTopRectangle(Graphics graphics, Brush brush, RectangleF rect, float radius)
    {
        using var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddLine(rect.Right, rect.Bottom, rect.X, rect.Bottom);
        path.CloseFigure();
        graphics.FillPath(brush, path);
    }

    private static GraphicsPath CreateRoundedRectanglePath(RectangleF rect, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
