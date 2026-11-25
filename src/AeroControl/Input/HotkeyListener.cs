using System;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AeroControl.Input;

public enum HotkeyKind
{
    BrightnessDown, BrightnessUp, FanKey, MicMute, KeyboardLight,
    TouchpadToggled, FnLock, WinLock, Wifi, Camera, Unknown,
}

public sealed class HotkeyEventArgs : EventArgs
{
    public HotkeyKind Kind;
    public int Value = -1;      // on/off (1/0) or level, when the event carries one
    public string Raw;
}

/// <summary>
/// Fn-key sources GCC's OSDwindow listened to, now handled here:
///  1. Keyboard (ITE 0414:7A3A) vendor HID collection, usage page 0xFF02 / usage 1, read through Raw Input.
///     Reports are 4 bytes [04, b1, b2, b3]:
///       b1=0,b2=0,b3=125/126 brightness down/up; 132 fan key; 146 mic; 148/149 Win-lock/Fn-lock (b2 = on);
///       b1=1 keyboard backlight changed (b2 = level).
///  2. Firmware WMI event GB_WMIACPI_Event, Data[0]: 200 mic, 201 Fn-lock, 202 touchpad, 203 Win-lock,
///     246 keyboard light (Data[1] = state).
/// Every raw event is logged, so unmapped keys on this model can be identified.
/// </summary>
public sealed class HotkeyListener : IDisposable
{
    public event EventHandler<HotkeyEventArgs> Hotkey;

    private readonly HwndSource _sink;
    private ManagementEventWatcher _wmi;

    public HotkeyListener()
    {
        // message-only window as the raw-input target (INPUTSINK: receive while in the background)
        _sink = new HwndSource(new HwndSourceParameters("AeroControl.Hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0 });
        _sink.AddHook(WndProc);
        var devs = new[]
        {
            new RAWINPUTDEVICE { usUsagePage = 0xFF02, usUsage = 0x0001, dwFlags = RIDEV_INPUTSINK, hwndTarget = _sink.Handle },
            new RAWINPUTDEVICE { usUsagePage = 0xFF01, usUsage = 0x2209, dwFlags = RIDEV_INPUTSINK, hwndTarget = _sink.Handle },
            new RAWINPUTDEVICE { usUsagePage = 0xFF00, usUsage = 0xFF00, dwFlags = RIDEV_INPUTSINK, hwndTarget = _sink.Handle },
        };
        bool ok = RegisterRawInputDevices(devs, (uint)devs.Length, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
        Log.Write($"Hotkeys: raw input registered={ok} (err {(ok ? 0 : Marshal.GetLastWin32Error())})");

        try
        {
            _wmi = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new EventQuery("SELECT * FROM GB_WMIACPI_Event"));
            _wmi.EventArrived += OnWmiEvent;
            _wmi.Start();
            Log.Write("Hotkeys: firmware event watcher started");
        }
        catch (Exception ex) { Log.Write("Hotkeys: WMI watcher failed: " + ex.Message); }
    }

    private void Raise(HotkeyKind k, int value, string raw)
    {
        Log.Write($"Hotkey {k} value={value} raw={raw}");
        try { Hotkey?.Invoke(this, new HotkeyEventArgs { Kind = k, Value = value, Raw = raw }); }
        catch (Exception ex) { Log.Write("Hotkey handler: " + ex); }
    }

    // ---------- keyboard (raw input) ----------

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_INPUT) return IntPtr.Zero;
        uint size = 0;
        int hdr = Marshal.SizeOf<RAWINPUTHEADER>();
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, hdr);
        if (size == 0) return IntPtr.Zero;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, hdr) != size) return IntPtr.Zero;
            var h = Marshal.PtrToStructure<RAWINPUTHEADER>(buf);
            if (h.dwType != RIM_TYPEHID) return IntPtr.Zero;
            int sizeHid = Marshal.ReadInt32(buf, hdr);
            int count = Marshal.ReadInt32(buf, hdr + 4);
            var data = new byte[sizeHid * Math.Max(1, count)];
            Marshal.Copy(buf + hdr + 8, data, 0, data.Length);
            HandleKeyboardReport(data.Take(sizeHid).ToArray());
        }
        finally { Marshal.FreeHGlobal(buf); }
        return IntPtr.Zero;
    }

    private void HandleKeyboardReport(byte[] r)
    {
        string raw = "kb " + BitConverter.ToString(r);
        if (r.Length < 4 || r[0] != 4) { Log.Write("Hotkey unmapped " + raw); return; }
        byte b1 = r[1], b2 = r[2], b3 = r[3];
        if (b1 == 0 && (b2 == 0 || b2 == 1))
        {
            switch (b3)
            {
                case 125: Raise(HotkeyKind.BrightnessDown, -1, raw); return;
                case 126: Raise(HotkeyKind.BrightnessUp, -1, raw); return;
                case 129: Raise(HotkeyKind.TouchpadToggled, -1, raw); return; // Fn+F10 on the AERO 14
                case 132: Raise(HotkeyKind.FanKey, -1, raw); return;            // Fn+Esc
                case 146: Raise(HotkeyKind.MicMute, -1, raw); return;
                case 148: Raise(HotkeyKind.WinLock, b2, raw); return;
                case 149: Raise(HotkeyKind.FnLock, b2, raw); return;
                case 133: case 134: case 135: return; // volume: Windows shows its own flyout
            }
        }
        if (b1 == 1) { Raise(HotkeyKind.KeyboardLight, Math.Min(100, b2 * 2), raw); return; } // Fn+Space: level 0..50 → %
        Log.Write("Hotkey unmapped " + raw);
    }

    // ---------- firmware (WMI) ----------

    private void OnWmiEvent(object sender, EventArrivedEventArgs e)
    {
        if (!(e.NewEvent["Data"] is byte[] d) || d.Length == 0) return;
        string raw = "wmi " + BitConverter.ToString(d.Take(4).ToArray());
        int v = d.Length > 1 ? (d[1] != 0 ? 1 : 0) : -1;
        switch (d[0])
        {
            case 200: Raise(HotkeyKind.MicMute, -1, raw); break;
            case 201: Raise(HotkeyKind.FnLock, v, raw); break;
            case 202: Raise(HotkeyKind.TouchpadToggled, v, raw); break;
            case 203: Raise(HotkeyKind.WinLock, d.Length > 1 && d[1] == 0 ? 1 : 0, raw); break;
            case 246: Raise(HotkeyKind.KeyboardLight, -1, raw); break;
            default: Log.Write("Hotkey unmapped " + raw); break;
        }
    }

    public void Dispose()
    {
        try { _wmi?.Stop(); _wmi?.Dispose(); } catch { }
        _sink?.Dispose();
    }

    // ---------- interop ----------

    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003, RIDEV_INPUTSINK = 0x00000100, RIM_TYPEHID = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE { public ushort usUsagePage; public ushort usUsage; public uint dwFlags; public IntPtr hwndTarget; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER { public uint dwType; public uint dwSize; public IntPtr hDevice; public IntPtr wParam; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint command, IntPtr data, ref uint size, int headerSize);
}
