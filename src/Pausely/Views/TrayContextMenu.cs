using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Pausely.Views;

/// <summary>Retains the native tray-menu behavior with a quieter, modern presentation.</summary>
internal sealed class TrayContextMenu : Forms.ContextMenuStrip
{
    private readonly Font _menuFont = new("Segoe UI", 9f);

    public TrayContextMenu()
    {
        Font = _menuFont;
        ShowImageMargin = false;
        ShowCheckMargin = false;
        DropShadowEnabled = true;
        ApplyAppearance();
    }

    public void ShowFromTray()
    {
        if (Visible) return;
        Show(Forms.Cursor.Position);
        // A manually opened tray popup must own foreground focus to dismiss on outside clicks.
        _ = SetForegroundWindow(Handle);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Windows 11 can round custom popup menus without replacing their native behavior.
        // Older/unsupported desktop compositions simply retain their standard menu shape.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var rounded = 2; // DWMWCP_ROUND
            _ = DwmSetWindowAttribute(Handle, 33, ref rounded, sizeof(int));
        }
    }

    protected override void OnOpening(CancelEventArgs e)
    {
        ApplyAppearance();
        base.OnOpening(e);
    }

    protected override void OnItemAdded(Forms.ToolStripItemEventArgs e)
    {
        base.OnItemAdded(e);
        if (e.Item is { } item) SizeItem(item);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        var highContrast = Forms.SystemInformation.HighContrast;
        BackColor = highContrast ? System.Drawing.SystemColors.Menu : Color.White;
        ForeColor = highContrast ? System.Drawing.SystemColors.MenuText : Color.FromArgb(32, 37, 43);
        Renderer = highContrast ? new Forms.ToolStripSystemRenderer() : new MenuRenderer();
        Padding = new Forms.Padding(0, Scale(8), 0, Scale(8));
        MinimumSize = new System.Drawing.Size(Scale(280), 0);
        foreach (Forms.ToolStripItem item in Items) SizeItem(item);
    }

    private void SizeItem(Forms.ToolStripItem item)
    {
        item.AutoSize = false;
        item.Margin = Forms.Padding.Empty;
        item.Padding = Forms.Padding.Empty;
        item.Size = new System.Drawing.Size(Scale(280), Scale(item is Forms.ToolStripSeparator ? 13 : 32));
    }

    private int Scale(int value) => (int)Math.Round(value * DeviceDpi / 96d);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _menuFont.Dispose();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    private sealed class MenuRenderer : Forms.ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(Forms.ToolStripRenderEventArgs e)
            => e.Graphics.Clear(Color.White);

        protected override void OnRenderToolStripBorder(Forms.ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(232, 235, 239));
            var bounds = e.ToolStrip.ClientRectangle;
            bounds.Width--;
            bounds.Height--;
            e.Graphics.DrawRectangle(pen, bounds);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var scale = (e.ToolStrip?.DeviceDpi ?? 96) / 96f;
            var bounds = new RectangleF(6 * scale, 1 * scale, e.Item.Width - 12 * scale, e.Item.Height - 2 * scale);
            using var shape = RoundedRectangle(bounds, 5 * scale);
            using var brush = new SolidBrush(Color.FromArgb(241, 244, 248));
            var previous = e.Graphics.SmoothingMode;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.FillPath(brush, shape);
            e.Graphics.SmoothingMode = previous;
        }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            var inset = (int)Math.Round(20 * (e.ToolStrip?.DeviceDpi ?? 96) / 96d);
            var bounds = new Rectangle(inset, 0, e.Item.Width - inset * 2, e.Item.Height);
            var color = e.Item.Enabled ? Color.FromArgb(32, 37, 43) : Color.FromArgb(110, 118, 129);
            var flags = Forms.TextFormatFlags.Left | Forms.TextFormatFlags.VerticalCenter |
                Forms.TextFormatFlags.SingleLine | Forms.TextFormatFlags.EndEllipsis | Forms.TextFormatFlags.NoPadding;
            flags |= e.TextFormat & Forms.TextFormatFlags.HidePrefix;
            Forms.TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, bounds, color, flags);
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var pen = new Pen(Color.FromArgb(220, 229, 241));
            var middle = e.Item.Height / 2;
            e.Graphics.DrawLine(pen, 0, middle, e.Item.Width, middle);
        }

        private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            var diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
