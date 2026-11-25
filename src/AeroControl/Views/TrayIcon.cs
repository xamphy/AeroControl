using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AeroControl.Views;

/// <summary>Tray icon: left click toggles the popover, right click shows a small menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;

    public TrayIcon(Action onClick, Action onOpen, Action onExit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Aero Control", null, (_, _) => onOpen());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => onExit());
        _icon = new NotifyIcon { Icon = Draw(), Text = "Aero Control", Visible = true, ContextMenuStrip = menu };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) onClick(); };
    }

    public void SetTooltip(string s) => _icon.Text = s.Length > 63 ? s.Substring(0, 63) : s;

    /// <summary>A small white gauge on transparent, like a system tray glyph.</summary>
    private static System.Drawing.Icon Draw()
    {
        int sz = SystemInformation.SmallIconSize.Width;
        using var bmp = new Bitmap(sz, sz);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float s = sz / 24f;
            using var pen = new Pen(Color.White, 2.2f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawArc(pen, 3 * s, 5 * s, 18 * s, 18 * s, 160, 220);
            g.DrawLine(pen, 12 * s, 14 * s, 16.5f * s, 9.5f * s);
        }
        IntPtr h = bmp.GetHicon();
        var icon = (System.Drawing.Icon)System.Drawing.Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }

    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
