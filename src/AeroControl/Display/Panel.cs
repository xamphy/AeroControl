using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AeroControl.Display;

/// <summary>
/// Internal OLED panel: refresh rate (what GCC delegates to OSDwindow.exe over the "OSDSvc" pipe),
/// and backlight/brightness via the standard WmiMonitorBrightness classes.
/// </summary>
public static class Panel
{
    /// <summary>GDI device name of the internal panel (\\.\DISPLAYn), or the primary screen as fallback.</summary>
    public static string DeviceName
    {
        get
        {
            // The internal panel is the one whose monitor device id is the integrated "SDC"/eDP panel;
            // on this laptop it is the only built-in display, so prefer the primary unless an internal one is found.
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            for (uint i = 0; EnumDisplayDevices(null, i, ref dd, 0); i++, dd.cb = Marshal.SizeOf<DISPLAY_DEVICE>())
            {
                if ((dd.StateFlags & 1) == 0) continue; // attached to desktop
                var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
                if (EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) && IsInternal(mon.DeviceID))
                    return dd.DeviceName;
            }
            return Screen.PrimaryScreen.DeviceName;
        }
    }

    private static bool IsInternal(string monitorId)
    {
        try
        {
            foreach (ManagementObject m in new ManagementObjectSearcher(@"root\WMI", "SELECT InstanceName, VideoOutputTechnology FROM WmiMonitorConnectionParams").Get())
            {
                var inst = (string)m["InstanceName"];
                uint tech = Convert.ToUInt32(m["VideoOutputTechnology"]);
                // 0x80000000 = internal, 11 = DisplayPort embedded (eDP)
                if ((tech == 0x80000000 || tech == 11) && monitorId != null && inst != null)
                {
                    var code = inst.Split('\\').ElementAtOrDefault(1);
                    if (code != null && monitorId.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }
        }
        catch { }
        return false;
    }

    public static int CurrentRefreshRate
    {
        get
        {
            var dm = NewDevMode();
            return EnumDisplaySettings(DeviceName, ENUM_CURRENT_SETTINGS, ref dm) ? dm.dmDisplayFrequency : 0;
        }
    }

    /// <summary>Refresh rates the panel offers at its current resolution, highest first.</summary>
    public static IReadOnlyList<int> AvailableRefreshRates
    {
        get
        {
            string dev = DeviceName;
            var cur = NewDevMode();
            if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref cur)) return Array.Empty<int>();
            var rates = new SortedSet<int>();
            var dm = NewDevMode();
            for (int i = 0; EnumDisplaySettings(dev, i, ref dm); i++)
                if (dm.dmPelsWidth == cur.dmPelsWidth && dm.dmPelsHeight == cur.dmPelsHeight && dm.dmBitsPerPel == cur.dmBitsPerPel)
                    rates.Add(dm.dmDisplayFrequency);
            return rates.Reverse().ToList();
        }
    }

    /// <summary>Switch the panel's refresh rate, keeping resolution. Returns true on success.</summary>
    public static bool SetRefreshRate(int hz)
    {
        string dev = DeviceName;
        var dm = NewDevMode();
        if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        if (dm.dmDisplayFrequency == hz) return true;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = DM_DISPLAYFREQUENCY;
        int r = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        Log.Write($"Panel.SetRefreshRate({hz}) on {dev} -> {r}");
        return r == 0;
    }

    public static (int w, int h) CurrentResolution
    {
        get
        {
            var dm = NewDevMode();
            return EnumDisplaySettings(DeviceName, ENUM_CURRENT_SETTINGS, ref dm) ? (dm.dmPelsWidth, dm.dmPelsHeight) : (0, 0);
        }
    }

    /// <summary>All (width, height) pairs the panel offers, largest first.</summary>
    public static IReadOnlyList<(int w, int h)> AvailableResolutions
    {
        get
        {
            string dev = DeviceName;
            var set = new HashSet<(int, int)>();
            var dm = NewDevMode();
            for (int i = 0; EnumDisplaySettings(dev, i, ref dm); i++)
                if (dm.dmBitsPerPel == 32) set.Add((dm.dmPelsWidth, dm.dmPelsHeight));
            return set.OrderByDescending(r => r.Item1 * r.Item2).ToList();
        }
    }

    /// <summary>
    /// Switch resolution and refresh rate together in one mode change (one blank instead of two).
    /// Not something GCC does; it backs our optional "lower resolution on battery" setting.
    /// </summary>
    public static bool SetMode(int width, int height, int hz)
    {
        string dev = DeviceName;
        var dm = NewDevMode();
        if (!EnumDisplaySettings(dev, ENUM_CURRENT_SETTINGS, ref dm)) return false;
        if (dm.dmPelsWidth == width && dm.dmPelsHeight == height && dm.dmDisplayFrequency == hz) return true;
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmDisplayFrequency = hz;
        dm.dmFields = DM_PELSWIDTH | DM_PELSHEIGHT | DM_DISPLAYFREQUENCY;
        int test = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_TEST, IntPtr.Zero);
        if (test != 0) { Log.Write($"Panel.SetMode({width}x{height}@{hz}) rejected by driver ({test})"); return false; }
        int r = ChangeDisplaySettingsEx(dev, ref dm, IntPtr.Zero, CDS_UPDATEREGISTRY, IntPtr.Zero);
        Log.Write($"Panel.SetMode({width}x{height}@{hz}) on {dev} -> {r}");
        return r == 0;
    }

    /// <summary>
    /// Power-source display profile. AC: native resolution at the highest rate.
    /// Battery: lowest rate, and optionally a lower resolution (e.g. 1920x1200).
    /// </summary>
    public static void ApplyPowerProfile(bool onAc, bool autoRefresh, (int w, int h)? batteryResolution)
    {
        var res = AvailableResolutions;
        if (res.Count == 0) return;
        var native = res[0];
        var target = onAc || batteryResolution == null ? native : batteryResolution.Value;
        int hz = CurrentRefreshRate;
        if (autoRefresh)
        {
            var rates = AvailableRefreshRates;
            if (rates.Count > 0) hz = onAc ? rates.First() : rates.Last();
        }
        SetMode(target.w, target.h, hz);
    }

    /// <summary>GCC's "Auto Panel Refresh Rate": highest rate on AC, lowest on battery.</summary>
    public static void ApplyAutoRefresh(bool onAc)
    {
        var rates = AvailableRefreshRates;
        if (rates.Count == 0) return;
        SetRefreshRate(onAc ? rates.First() : rates.Last());
    }

    // ---- brightness (standard Windows WMI, same values GCC stores as acL/dcL) ----

    public static int GetBrightness()
    {
        try
        {
            foreach (ManagementObject o in new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness").Get())
                return Convert.ToInt32(o["CurrentBrightness"]);
        }
        catch { }
        return -1;
    }

    public static void SetBrightness(int percent)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        try
        {
            foreach (ManagementObject o in new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods").Get())
            {
                o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                return;
            }
        }
        catch (Exception ex) { Log.Write("SetBrightness failed: " + ex.Message); }
    }

    // ---- interop ----

    private const int ENUM_CURRENT_SETTINGS = -1;
    private const int DM_DISPLAYFREQUENCY = 0x400000;
    private const int CDS_UPDATEREGISTRY = 0x01;
    private const int CDS_TEST = 0x02;
    private const int DM_PELSWIDTH = 0x80000;
    private const int DM_PELSHEIGHT = 0x100000;

    private static DEVMODE NewDevMode() => new() { dmSize = (short)Marshal.SizeOf<DEVMODE>() };

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public int dmFields;
        public int dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplaySettings(string dev, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int ChangeDisplaySettingsEx(string dev, ref DEVMODE dm, IntPtr hwnd, int flags, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern bool EnumDisplayDevices(string dev, uint i, ref DISPLAY_DEVICE dd, uint flags);
}
