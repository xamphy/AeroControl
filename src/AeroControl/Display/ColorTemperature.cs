using System;
using System.Runtime.InteropServices;

namespace AeroControl.Display;

/// <summary>
/// Panel white point via the GDI gamma ramp (the mechanism GCC uses for its "blue hue" slider in CColor).
/// GCC's older models shipped per-panel ICC files for 5800K/6000K/6500K/6800K; on X-Rite models like the
/// AERO 14 it relies on the separate X-Rite Color Assistant instead (not installed here), so we compute the
/// white point directly. 6500K = native (identity ramp).
/// </summary>
public static class ColorTemperature
{
    public const int Native = 6500;
    public static readonly int[] Presets = { 5800, 6000, 6500, 6800 };

    /// <summary>Apply a white point in Kelvin (clamped 4500..8000) plus an optional extra warmth 0..1 on blue.</summary>
    public static bool Apply(int kelvin)
    {
        kelvin = Math.Max(4500, Math.Min(8000, kelvin));
        var (r, g, b) = Multipliers(kelvin);
        var ramp = new RAMP { Red = new ushort[256], Green = new ushort[256], Blue = new ushort[256] };
        for (int i = 0; i < 256; i++)
        {
            ramp.Red[i] = (ushort)Math.Min(65535, i * 257 * r);
            ramp.Green[i] = (ushort)Math.Min(65535, i * 257 * g);
            ramp.Blue[i] = (ushort)Math.Min(65535, i * 257 * b);
        }
        var hdc = CreateDC(null, Panel.DeviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero) return false;
        try
        {
            bool ok = SetDeviceGammaRamp(hdc, ref ramp);
            Log.Write($"ColorTemperature.Apply({kelvin}K) r={r:F3} g={g:F3} b={b:F3} -> {ok}");
            return ok;
        }
        finally { DeleteDC(hdc); }
    }

    public static bool Reset() => Apply(Native);

    /// <summary>RGB gains for a blackbody white point, normalised so 6500K = (1,1,1) and max channel = 1.</summary>
    private static (double r, double g, double b) Multipliers(int kelvin)
    {
        var t = Blackbody(kelvin);
        var n = Blackbody(Native);
        double r = t.r / n.r, g = t.g / n.g, b = t.b / n.b;
        double max = Math.Max(r, Math.Max(g, b));
        return (r / max, g / max, b / max);
    }

    // Tanner Helland's blackbody approximation (sRGB, 0..1).
    private static (double r, double g, double b) Blackbody(int kelvin)
    {
        double t = kelvin / 100.0, r, g, b;
        r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        static double C(double v) => Math.Max(0, Math.Min(255, v)) / 255.0;
        return (C(r), C(g), C(b));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public ushort[] Blue;
    }

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string drv, string dev, string o, IntPtr init);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP ramp);
}
