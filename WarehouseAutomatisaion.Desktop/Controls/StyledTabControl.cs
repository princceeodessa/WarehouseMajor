using System.Drawing.Drawing2D;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class StyledTabControl : TabControl
{
    public StyledTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(192, 38);
        Padding = new Point(20, 10);
        Multiline = false;
        Appearance = TabAppearance.Normal;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage tabPage)
        {
            tabPage.BackColor = DesktopTheme.AppBackground;
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= TabPages.Count)
        {
            return;
        }

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var tabPage = TabPages[e.Index];
        var bounds = GetTabRect(e.Index);
        bounds.Inflate(-6, -4);

        var selected = SelectedIndex == e.Index;
        var background = selected ? DesktopTheme.Surface : DesktopTheme.SurfaceMuted;
        var border = selected ? DesktopTheme.BorderStrong : DesktopTheme.Border;
        var textColor = selected ? DesktopTheme.TextPrimary : DesktopTheme.TextSecondary;

        using var path = CreateRoundRectangle(bounds, 12);
        using var backgroundBrush = new SolidBrush(background);
        using var borderPen = new Pen(border, 1f);
        using var accentBrush = new SolidBrush(DesktopTheme.Primary);
        using var textBrush = new SolidBrush(textColor);
        using var font = selected ? DesktopTheme.EmphasisFont(9.8f) : DesktopTheme.BodyFont(9.5f);
        var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        graphics.FillPath(backgroundBrush, path);
        graphics.DrawPath(borderPen, path);
        if (selected)
        {
            graphics.FillRectangle(accentBrush, new Rectangle(bounds.X + 14, bounds.Bottom - 3, Math.Max(0, bounds.Width - 28), 3));
        }

        graphics.DrawString(tabPage.Text, font, textBrush, bounds, format);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        using var borderPen = new Pen(DesktopTheme.Border);
        e.Graphics.DrawLine(borderPen, 0, ItemSize.Height + 10, Width, ItemSize.Height + 10);
    }

    private static GraphicsPath CreateRoundRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
