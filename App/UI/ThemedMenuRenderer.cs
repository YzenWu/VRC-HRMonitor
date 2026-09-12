using System.Drawing.Drawing2D;

namespace HeartRateMonitor.UI;

/// <summary>
/// Theme Menu Renderer: Allows the right-click menu of the tray to have the same color as the front end of Web.
/// Colours read <see cref="ThemeColors"/> in real time every time you draw them (the menu is not recreated after changing the theme in the settings).
/// </summary>
public sealed class ThemedMenuRenderer : ToolStripProfessionalRenderer
{
    public ThemedMenuRenderer() : base(new ThemedColorTable()) { }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item?.Enabled == false ? ThemeColors.Muted()
            : e.Item?.Selected == true ? ThemeColors.OnAccent()
            : ThemeColors.Foreground();
        base.OnRenderItemText(e);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var r = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
        using var brush = new SolidBrush(
            e.Item.Selected && e.Item.Enabled ? ThemeColors.Accent() : ThemeColors.Background());
        var radius = Math.Min(ThemeColors.MenuRadius(), r.Height / 2);
        if (radius <= 1)
        {
            e.Graphics.FillRectangle(brush, r);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(r, radius);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(ThemeColors.Background());
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var radius = ThemeColors.MenuRadius();
        var r = new Rectangle(0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        using var pen = new Pen(ThemeColors.Border());
        if (radius <= 1)
        {
            e.Graphics.DrawRectangle(pen, r);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = RoundedPath(r, radius);
        e.Graphics.DrawPath(pen, path);
        // It's still a square window outside the round corner, which will reveal the bottom colour of the system and cut it off with the window area.
        if (e.ToolStrip is ToolStripDropDown dd)
        {
            using var outline = RoundedPath(new Rectangle(0, 0, dd.Width, dd.Height), radius);
            dd.Region = new Region(outline);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(ThemeColors.Border());
        int y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    private static GraphicsPath RoundedPath(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>XQ1QXZ only affects a few details, and the main color is determined by OnRender* above. </summary>
    private sealed class ThemedColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected => ThemeColors.Accent();
        public override Color MenuItemSelectedGradientBegin => ThemeColors.Accent();
        public override Color MenuItemSelectedGradientEnd => ThemeColors.Accent();
        public override Color MenuItemBorder => ThemeColors.Accent();
        public override Color MenuBorder => ThemeColors.Border();
        public override Color ToolStripDropDownBackground => ThemeColors.Background();
        public override Color ImageMarginGradientBegin => ThemeColors.Background();
        public override Color ImageMarginGradientMiddle => ThemeColors.Background();
        public override Color ImageMarginGradientEnd => ThemeColors.Background();
        public override Color CheckBackground => ThemeColors.Hover();
        public override Color CheckSelectedBackground => ThemeColors.Accent();
    }
}
