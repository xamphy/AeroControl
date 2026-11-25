using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace AeroControl.Display;

/// <summary>
/// OLED Care, re-implemented from GCC (ucNotebook.Helper.OledCare + GeneralVD timers):
///  - auto-hide taskbar, dark mode, taskbar transparency (Windows settings)
///  - Pixel Shift: after 30 min idle, nudge the desktop icon layer by 1 px every 3 min (4-step cycle)
///  - ASC (Automatic Static Control): after 5 min idle dim the panel by 5, after 10 min by 10; restore on input
/// GCC routes Pixel Shift through OSDwindow.exe; we do it directly so it works without that helper.
/// </summary>
public sealed class OledCare : IDisposable
{
    private const string Personalize = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // ---------- Windows appearance ----------

    public static bool DarkMode
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Personalize);
            return k != null && Convert.ToInt32(k.GetValue("AppsUseLightTheme", 1)) == 0
                             && Convert.ToInt32(k.GetValue("SystemUsesLightTheme", 1)) == 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Personalize);
            k.SetValue("AppsUseLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
            k.SetValue("SystemUsesLightTheme", value ? 0 : 1, RegistryValueKind.DWord);
            BroadcastSettingChange("ImmersiveColorSet"); // no explorer restart needed (GCC kills explorer)
        }
    }

    public static bool TaskbarTransparency
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(Personalize);
            return k != null && Convert.ToInt32(k.GetValue("EnableTransparency", 0)) != 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(Personalize);
            k.SetValue("EnableTransparency", value ? 1 : 0, RegistryValueKind.DWord);
            BroadcastSettingChange("ImmersiveColorSet");
        }
    }

    public static bool TaskbarAutoHide
    {
        get
        {
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = FindWindow("Shell_TrayWnd", null) };
            return (SHAppBarMessage(ABM_GETSTATE, ref abd).ToInt32() & ABS_AUTOHIDE) != 0;
        }
        set
        {
            // ABM_SETSTATE applies immediately on Windows 11 too, without GCC's explorer restart.
            var abd = new APPBARDATA
            {
                cbSize = Marshal.SizeOf<APPBARDATA>(),
                hWnd = FindWindow("Shell_TrayWnd", null),
                lParam = value ? (IntPtr)ABS_AUTOHIDE : (IntPtr)ABS_ALWAYSONTOP,
            };
            SHAppBarMessage(ABM_SETSTATE, ref abd);
        }
    }

    // ---------- Pixel Shift + ASC (idle-driven) ----------

    public bool PixelShiftEnabled { get; set; }
    public bool AscEnabled { get; set; }

    private readonly Timer _timer;
    private int _shiftStep;
    private long _lastShiftIdle;
    private int _ascRestore = -1;   // brightness to restore, -1 = not dimmed
    private int _ascLevel;          // 0 none, 1 = -5, 2 = -10

    public OledCare()
    {
        _timer = new Timer(_ => Tick(), null, 1000, 1000);
    }

    private void Tick()
    {
        try
        {
            int idle = IdleSeconds();

            // ASC
            if (AscEnabled)
            {
                if (idle <= 1 && _ascRestore >= 0)
                {
                    Panel.SetBrightness(_ascRestore);
                    _ascRestore = -1; _ascLevel = 0;
                }
                else if (idle >= 300 && _ascLevel == 0) Dim(5, 1);
                else if (idle >= 600 && _ascLevel == 1) Dim(10, 2);
            }
            else if (_ascRestore >= 0) { Panel.SetBrightness(_ascRestore); _ascRestore = -1; _ascLevel = 0; }

            // Pixel Shift: idle > 30 min, then every 3 min
            if (PixelShiftEnabled && idle > 1800 && idle - _lastShiftIdle >= 180)
            {
                _lastShiftIdle = idle;
                var (x, y) = (_shiftStep++ % 4) switch { 0 => (1, 0), 1 => (1, 1), 2 => (0, 1), _ => (0, 0) };
                MoveDesktopIcons(x, y);
            }
            else if (idle < 1800 && _lastShiftIdle != 0)
            {
                _lastShiftIdle = 0;
            }
        }
        catch (Exception ex) { Log.Write("OledCare tick: " + ex.Message); }
    }

    private void Dim(int by, int level)
    {
        int now = Panel.GetBrightness();
        if (now <= 0) return;
        if (_ascRestore < 0) _ascRestore = now;
        Panel.SetBrightness(Math.Max(0, _ascRestore - by));
        _ascLevel = level;
    }

    private static void MoveDesktopIcons(int left, int top)
    {
        IntPtr lv = IntPtr.Zero;
        EnumWindows((h, _) =>
        {
            var view = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero) return true;
            lv = FindWindowEx(view, IntPtr.Zero, "SysListView32", "FolderView");
            return false;
        }, IntPtr.Zero);
        if (lv == IntPtr.Zero || !GetWindowRect(lv, out var r)) return;
        MoveWindow(lv, left, top, r.Right - r.Left, r.Bottom - r.Top, true);
    }

    public void Dispose()
    {
        _timer.Dispose();
        if (_ascRestore >= 0) Panel.SetBrightness(_ascRestore);
    }

    // ---------- interop ----------

    private static int IdleSeconds()
    {
        var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref li) ? (int)((uint)Environment.TickCount - li.dwTime) / 1000 : 0;
    }

    private static void BroadcastSettingChange(string area) =>
        SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, area, 2, 1000, out _);

    private const int ABM_GETSTATE = 4, ABM_SETSTATE = 10, ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(int msg, ref APPBARDATA d);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string cls, string title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc f, IntPtr l);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr h, int x, int y, int w, int hh, bool repaint);
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO li);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr h, int msg, IntPtr w, string l, int flags, int timeout, out IntPtr result);
}
