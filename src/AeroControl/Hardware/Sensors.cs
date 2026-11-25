using System.Windows.Forms;

namespace AeroControl.Hardware;

public sealed class SensorSnapshot
{
    public int CpuTemp = -1, GpuTemp = -1, Fan1Rpm = -1, Fan2Rpm = -1, Fan1Pct = -1, Fan2Pct = -1;
    public int BatteryPercent = -1;
    public bool OnAc = true;
}

public static class Sensors
{
    public static SensorSnapshot Read()
    {
        var s = new SensorSnapshot();
        if (GbWmi.TryGet("getCpuTemp", out var c)) s.CpuTemp = c;
        if (GbWmi.TryGet("getGpuTemp1", out var g)) s.GpuTemp = g;
        if (GbWmi.TryGet("getRpm1", out var r1)) s.Fan1Rpm = Swap(r1);
        if (GbWmi.TryGet("getRpm2", out var r2)) s.Fan2Rpm = Swap(r2);
        if (GbWmi.TryGet("GetCPUFanDuty", out var d1)) s.Fan1Pct = Fans.DutyToPercent(d1);
        if (GbWmi.TryGet("GetGPUFanDuty", out var d2)) s.Fan2Pct = Fans.DutyToPercent(d2);
        var ps = SystemInformation.PowerStatus;
        s.OnAc = ps.PowerLineStatus != PowerLineStatus.Offline;
        if (ps.BatteryLifePercent <= 1f) s.BatteryPercent = (int)(ps.BatteryLifePercent * 100);
        return s;
    }

    /// <summary>The EC returns RPM byte-swapped (50955 = 0xC70B -> 0x0BC7 = 3015 rpm).</summary>
    private static int Swap(int v) => ((v & 0xFF) << 8) | ((v >> 8) & 0xFF);

    public static bool OnAc => SystemInformation.PowerStatus.PowerLineStatus != PowerLineStatus.Offline;
}
