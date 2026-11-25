using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace AeroControl.Hardware;

/// <summary>
/// White keyboard backlight on the AERO 14 (internal USB keyboard VID 0414 / PID 7A3A, ITE controller).
/// Protocol (decoded from GCC's KeyboardModel IteKeyBoard): 9-byte HID feature reports,
///   [0]=report id 0, [1]=command, [2..7]=args, [8]=255 - sum([1..7]).
///   0x08 set effect: [2]=sync [3]=type [4]=speed [5]=brightness(0..50, = percent/2) [6]=color [7]=direction
///   0x88 query effect (then GetFeature returns the same layout)
///   0x0A auto-off after one minute: [2]=1 on / 0 off
/// </summary>
public sealed class KeyboardLight : IDisposable
{
    private const ushort Vid = 0x0414, Pid = 0x7A3A;
    private readonly SafeFileHandle _h;

    private KeyboardLight(SafeFileHandle h) { _h = h; }

    public static KeyboardLight Open()
    {
        foreach (var path in Hid.EnumeratePaths())
        {
            var h = Hid.CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h.IsInvalid) continue;
            var attr = new Hid.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<Hid.HIDD_ATTRIBUTES>() };
            if (Hid.HidD_GetAttributes(h, ref attr) && attr.VendorID == Vid && attr.ProductID == Pid
                && Hid.FeatureLength(h) == 9)
                return new KeyboardLight(h);
            h.Dispose();
        }
        return null;
    }

    private static byte[] Packet(byte cmd, params byte[] args)
    {
        var b = new byte[9];
        b[1] = cmd;
        for (int i = 0; i < args.Length && i < 6; i++) b[2 + i] = args[i];
        int sum = 0;
        for (int i = 1; i <= 7; i++) sum += b[i];
        b[8] = (byte)(255 - (byte)sum);
        return b;
    }

    /// <summary>Raw query: sends a query command and returns the 9-byte reply.</summary>
    public byte[] Query(byte cmd)
    {
        var p = Packet(cmd);
        if (!Hid.HidD_SetFeature(_h, p, p.Length)) return null;
        System.Threading.Thread.Sleep(15);
        var r = new byte[9];
        return Hid.HidD_GetFeature(_h, r, r.Length) ? r : null;
    }

    /// <summary>Current brightness 0..100 (null if the keyboard did not answer).</summary>
    public int? GetBrightness()
    {
        var r = Query(0x88);
        return r == null ? null : Math.Min(100, r[5] * 2);
    }

    /// <summary>Set static white at 0..100%. Mirrors the state the keyboard reports (type/speed/color/dir = 0).</summary>
    public bool SetBrightness(int percent)
    {
        percent = Math.Max(0, Math.Min(100, percent));
        var p = Packet(0x08, 0, 0, 0, (byte)(percent / 2), 0, 0);
        return Hid.HidD_SetFeature(_h, p, p.Length);
    }

    public void Dispose() => _h?.Dispose();
}

internal static class Hid
{
    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES { public int Size; public ushort VendorID; public ushort ProductID; public ushort VersionNumber; }

    [StructLayout(LayoutKind.Sequential)]
    private struct HIDP_CAPS
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
            NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
            NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid g);
    [DllImport("hid.dll")] public static extern bool HidD_GetAttributes(SafeFileHandle h, ref HIDD_ATTRIBUTES a);
    [DllImport("hid.dll")] public static extern bool HidD_SetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] public static extern bool HidD_GetFeature(SafeFileHandle h, byte[] buf, int len);
    [DllImport("hid.dll")] private static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
    [DllImport("hid.dll")] private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr dev, ref Guid g, int idx, ref SP_DEVICE_INTERFACE_DATA d);
    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int req, IntPtr info);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    public static int FeatureLength(SafeFileHandle h)
    {
        if (!HidD_GetPreparsedData(h, out var pd)) return 0;
        try { HidP_GetCaps(pd, out var caps); return caps.FeatureReportByteLength; }
        finally { HidD_FreePreparsedData(pd); }
    }

    public static System.Collections.Generic.IEnumerable<string> EnumeratePaths()
    {
        HidD_GetHidGuid(out var g);
        var set = SetupDiGetClassDevs(ref g, IntPtr.Zero, IntPtr.Zero, 0x12); // PRESENT | DEVICEINTERFACE
        try
        {
            for (int i = 0; ; i++)
            {
                var d = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref g, i, ref d)) yield break;
                SetupDiGetDeviceInterfaceDetail(set, ref d, IntPtr.Zero, 0, out int req, IntPtr.Zero);
                var buf = Marshal.AllocHGlobal(req);
                string path = null;
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (SetupDiGetDeviceInterfaceDetail(set, ref d, buf, req, out _, IntPtr.Zero))
                        path = Marshal.PtrToStringUni(buf + 4);
                }
                finally { Marshal.FreeHGlobal(buf); }
                if (path != null) yield return path;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
    }
}
