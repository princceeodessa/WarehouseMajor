using System.Drawing.Drawing2D;

namespace WarehouseAutomatisaion.Desktop.Controls;

public static class DesktopTheme
{
    public static Color AppBackground => Color.FromArgb(246, 248, 253);

    public static Color SidebarBackground => Color.FromArgb(248, 250, 255);

    public static Color SidebarBorder => Color.FromArgb(221, 229, 238);

    public static Color SidebarButton => Color.FromArgb(255, 255, 255);

    public static Color SidebarButtonHover => Color.FromArgb(242, 246, 255);

    public static Color SidebarButtonActive => Color.FromArgb(236, 241, 255);

    public static Color SidebarButtonText => Color.FromArgb(38, 48, 66);

    public static Color SidebarButtonActiveText => Color.FromArgb(79, 99, 246);

    public static Color Surface => Color.FromArgb(255, 255, 255);

    public static Color SurfaceAlt => Color.FromArgb(251, 252, 255);

    public static Color SurfaceMuted => Color.FromArgb(243, 246, 255);

    public static Color Border => Color.FromArgb(224, 230, 244);

    public static Color BorderStrong => Color.FromArgb(208, 217, 237);

    public static Color HeaderBackground => Color.FromArgb(250, 252, 255);

    public static Color StatusBackground => Color.FromArgb(246, 248, 254);

    public static Color Primary => Color.FromArgb(84, 97, 245);

    public static Color PrimaryHover => Color.FromArgb(69, 82, 224);

    public static Color PrimarySoft => Color.FromArgb(236, 241, 255);

    public static Color PrimarySoftHover => Color.FromArgb(228, 234, 255);

    public static Color Warning => Color.FromArgb(201, 148, 43);

    public static Color Danger => Color.FromArgb(197, 76, 71);

    public static Color Info => Color.FromArgb(79, 99, 246);

    public static Color InfoSoft => Color.FromArgb(232, 238, 255);

    public static Color TextPrimary => Color.FromArgb(22, 34, 66);

    public static Color TextSecondary => Color.FromArgb(87, 101, 128);

    public static Color TextMuted => Color.FromArgb(127, 139, 161);

    public static Color GridSelectionBackground => Color.FromArgb(236, 241, 255);

    public static Color GridSelectionText => Color.FromArgb(65, 83, 190);

    public static Color ShadowSoft => Color.FromArgb(18, 18, 37, 63);

    public static Font TitleFont(float size = 20f) => new("Segoe UI Semibold", size, FontStyle.Bold);

    public static Font SubtitleFont(float size = 9.6f) => new("Segoe UI", size, FontStyle.Regular);

    public static Font BodyFont(float size = 9.6f) => new("Segoe UI", size, FontStyle.Regular);

    public static Font EmphasisFont(float size = 9.8f) => new("Segoe UI Semibold", size, FontStyle.Bold);
}

public enum DesktopButtonTone
{
    Primary,
    Secondary,
    Ghost
}

public static class DesktopSurfaceFactory
{
    public static Panel CreateCardShell()
    {
        return new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            BackColor = DesktopTheme.Surface,
            BorderColor = DesktopTheme.Border,
            BorderThickness = 1,
            CornerRadius = 14,
            DrawShadow = true,
            Margin = new Padding(0, 0, 14, 14)
        };
    }

    public static Panel CreateSidebarCard()
    {
        return new RoundedSurfacePanel
        {
            Dock = DockStyle.Top,
            Height = 108,
            BackColor = DesktopTheme.Surface,
            BorderColor = DesktopTheme.Border,
            BorderThickness = 1,
            CornerRadius = 12,
            DrawShadow = false,
            Padding = new Padding(14),
            Margin = new Padding(0, 0, 0, 12)
        };
    }

    public static Panel CreateCanvasPanel()
    {
        return new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DesktopTheme.AppBackground,
            Padding = new Padding(18, 14, 18, 18)
        };
    }

    public static StyledTabControl CreateTabControl()
    {
        return new StyledTabControl
        {
            Dock = DockStyle.Fill,
            Font = DesktopTheme.BodyFont()
        };
    }

    public static FlowLayoutPanel CreateToolbarStrip(
        bool wrapContents = true,
        int bottomPadding = 8)
    {
        return new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = wrapContents,
            AutoScroll = !wrapContents,
            Margin = new Padding(0),
            Padding = new Padding(0, 0, 0, Math.Max(0, bottomPadding))
        };
    }

    public static Button CreateActionButton(
        string text,
        EventHandler handler,
        DesktopButtonTone tone = DesktopButtonTone.Secondary,
        Padding? margin = null)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            Font = DesktopTheme.EmphasisFont(9.8f),
            Padding = new Padding(14, 9, 14, 9),
            Margin = margin ?? new Padding(8, 0, 0, 8),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false
        };

        button.FlatAppearance.MouseDownBackColor = Color.Empty;
        button.FlatAppearance.MouseOverBackColor = Color.Empty;

        ApplyTone(button, tone);
        button.Click += handler;
        return button;
    }

    public static Label CreateInfoChip(
        string text,
        Color? backColor = null,
        Color? foreColor = null)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            Padding = new Padding(10, 5, 10, 5),
            Margin = new Padding(0, 0, 8, 0),
            BackColor = backColor ?? DesktopTheme.SurfaceMuted,
            ForeColor = foreColor ?? DesktopTheme.TextSecondary,
            Font = DesktopTheme.EmphasisFont(9f)
        };
    }

    public static void ApplyTone(Button button, DesktopButtonTone tone)
    {
        button.FlatAppearance.BorderSize = tone == DesktopButtonTone.Primary ? 0 : 1;
        button.FlatAppearance.MouseDownBackColor = Color.Empty;
        button.FlatAppearance.MouseOverBackColor = Color.Empty;

        switch (tone)
        {
            case DesktopButtonTone.Primary:
                ConfigureInteractivePalette(
                    button,
                    DesktopTheme.Primary,
                    DesktopTheme.PrimaryHover,
                    DesktopTheme.PrimaryHover,
                    Color.White,
                    DesktopTheme.PrimaryHover);
                break;
            case DesktopButtonTone.Ghost:
                ConfigureInteractivePalette(
                    button,
                    DesktopTheme.Surface,
                    DesktopTheme.SurfaceMuted,
                    DesktopTheme.SurfaceMuted,
                    DesktopTheme.TextSecondary,
                    DesktopTheme.Border);
                break;
            default:
                ConfigureInteractivePalette(
                    button,
                    DesktopTheme.PrimarySoft,
                    DesktopTheme.PrimarySoftHover,
                    DesktopTheme.PrimarySoftHover,
                    DesktopTheme.SidebarButtonActiveText,
                    DesktopTheme.Border);
                break;
        }
    }

    private static void ConfigureInteractivePalette(
        Button button,
        Color normalBack,
        Color hoverBack,
        Color pressedBack,
        Color foreground,
        Color border)
    {
        button.BackColor = normalBack;
        button.ForeColor = foreground;
        button.FlatAppearance.BorderColor = border;

        button.MouseEnter += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = hoverBack;
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = normalBack;
            }
        };
        button.MouseDown += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = pressedBack;
            }
        };
        button.MouseUp += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = button.ClientRectangle.Contains(button.PointToClient(Cursor.Position)) ? hoverBack : normalBack;
            }
        };
        button.EnabledChanged += (_, _) =>
        {
            if (button.Enabled)
            {
                button.BackColor = normalBack;
                button.ForeColor = foreground;
                button.FlatAppearance.BorderColor = border;
            }
            else
            {
                button.BackColor = Color.FromArgb(237, 241, 247);
                button.ForeColor = DesktopTheme.TextSecondary;
                button.FlatAppearance.BorderColor = DesktopTheme.Border;
            }
        };
    }
}

public sealed class RoundedSurfacePanel : Panel
{
    public int CornerRadius { get; set; } = 12;

    public Color BorderColor { get; set; } = DesktopTheme.Border;

    public int BorderThickness { get; set; } = 1;

    public bool DrawShadow { get; set; }

    public RoundedSurfacePanel()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw, true);
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateRegion();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var contentRect = new Rectangle(0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        if (contentRect.Width < 2 || contentRect.Height < 2)
        {
            return;
        }

        if (DrawShadow)
        {
            var shadowRect = new Rectangle(contentRect.X + 2, contentRect.Y + 2, Math.Max(0, contentRect.Width - 2), Math.Max(0, contentRect.Height - 2));
            using var shadowPath = CreateRoundedPath(shadowRect, CornerRadius);
            using var shadowBrush = new SolidBrush(DesktopTheme.ShadowSoft);
            e.Graphics.FillPath(shadowBrush, shadowPath);
        }

        using var path = CreateRoundedPath(contentRect, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        using var borderPen = new Pen(BorderColor, BorderThickness);
        e.Graphics.FillPath(brush, path);
        if (BorderThickness > 0)
        {
            e.Graphics.DrawPath(borderPen, path);
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }

    private void UpdateRegion()
    {
        if (Width <= 2 || Height <= 2)
        {
            var oldRegion = Region;
            Region = null;
            oldRegion?.Dispose();
            return;
        }

        var maxRadius = Math.Max(1, Math.Min(Width, Height) / 2 - 1);
        var radius = Math.Clamp(CornerRadius, 1, maxRadius);

        using var path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), radius);
        using var region = new Region(path);
        var old = Region;
        Region = region.Clone();
        old?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
    {
        if (rect.Width <= 1 || rect.Height <= 1)
        {
            var emptyPath = new GraphicsPath();
            emptyPath.AddRectangle(rect);
            return emptyPath;
        }

        var maxRadius = Math.Max(1, Math.Min(rect.Width, rect.Height) / 2);
        var sanitizedRadius = Math.Clamp(radius, 1, maxRadius);
        var diameter = sanitizedRadius * 2;
        var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
        path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
        path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
