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

    /// <summary>The app icon at the tray's small-icon size (it carries 16/20/24/32 px frames).</summary>
    private static System.Drawing.Icon Draw()
    {
        var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/AeroControl.ico"));
        using var s = res.Stream;
        return new System.Drawing.Icon(s, SystemInformation.SmallIconSize);
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    public void Dispose() { _icon.Visible = false; _icon.Dispose(); }
}
