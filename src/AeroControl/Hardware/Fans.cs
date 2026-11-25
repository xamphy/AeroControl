using System;
using System.Collections.Generic;

namespace AeroControl.Hardware;

public enum FanMode { Eco, Normal, Power, Max, Fixed, Custom }

/// <summary>
/// Fan control, using the exact switch sequences GCC uses (ucNotebook.Helper.FanControl):
///   Normal : fixed 0, step 0, auto 0, quiet 0
///   Eco    : fixed 0, step 0, auto 0, quiet 1            (quiet = SetNvThermalTarget)
///   Power  : fixed 0, step 0, auto 1, quiet 0
///   Max    : auto 0, quiet 0, fixed speed 229, gpu duty 229, step 1, fixed 1
///   Fixed  : fixed 1, step 1, auto 0, quiet 0, fixed speed = gpu duty = d
///   Custom : fixed 0, step 1, auto 0, quiet 0, after writing the 15-point table
/// Duty values are 0..229 on the wire (229 = 100%).
/// </summary>
public static class Fans
{
    public const int FullDuty = 229;

    public static void Apply(FanMode mode, int fixedPercent = 50, IReadOnlyList<(int temp, int percent)> curve = null)
    {
        Log.Write($"Fans.Apply({mode})");
        switch (mode)
        {
            case FanMode.Normal:
                Seq(fixedSt: 0, step: 0, auto: 0, quiet: 0); break;
            case FanMode.Eco:
                Seq(fixedSt: 0, step: 0, auto: 0, quiet: 1); break;
            case FanMode.Power:
                Seq(fixedSt: 0, step: 0, auto: 1, quiet: 0); break;
            case FanMode.Max:
                GbWmi.TrySet("SetAutoFanStatus", 0);
                GbWmi.TrySet("SetNvThermalTarget", 0);
                GbWmi.TrySet("SetFixedFanSpeed", FullDuty);
                GbWmi.TrySet("SetGPUFanDuty", FullDuty);
                GbWmi.TrySet("SetStepFanStatus", 1);
                GbWmi.TrySet("SetFixedFanStatus", 1);
                break;
            case FanMode.Fixed:
                int d = PercentToDuty(fixedPercent);
                GbWmi.TrySet("SetFixedFanStatus", 1);
                GbWmi.TrySet("SetStepFanStatus", 1);
                GbWmi.TrySet("SetAutoFanStatus", 0);
                GbWmi.TrySet("SetNvThermalTarget", 0);
                GbWmi.TrySet("SetFixedFanSpeed", d);
                GbWmi.TrySet("SetGPUFanDuty", d);
                break;
            case FanMode.Custom:
                if (curve != null) WriteCurve(curve);
                GbWmi.TrySet("SetFixedFanStatus", 0);
                GbWmi.TrySet("SetStepFanStatus", 1);
                GbWmi.TrySet("SetAutoFanStatus", 0);
                GbWmi.TrySet("SetNvThermalTarget", 0);
                break;
        }
    }

    private static void Seq(int fixedSt, int step, int auto, int quiet)
    {
        GbWmi.TrySet("SetFixedFanStatus", fixedSt);
        GbWmi.TrySet("SetStepFanStatus", step);
        GbWmi.TrySet("SetAutoFanStatus", auto);
        GbWmi.TrySet("SetNvThermalTarget", quiet);
    }

    /// <summary>Infer the active mode from the EC switch state.</summary>
    public static FanMode? ReadMode()
    {
        if (!GbWmi.TryGet("GetFixedFanStatus", out var fx) || !GbWmi.TryGet("GetStepFanStatus", out var st)
            || !GbWmi.TryGet("GetAutoFanStatus", out var au) || !GbWmi.TryGet("GetNvThermalTarget", out var q))
            return null;
        if (fx == 1 && st == 1)
            return GbWmi.TryGet("GetFixedFanSpeed", out var sp) && sp >= FullDuty ? FanMode.Max : FanMode.Fixed;
        if (st == 1) return FanMode.Custom;
        if (au == 1) return FanMode.Power;
        return q == 1 ? FanMode.Eco : FanMode.Normal;
    }

    public static List<(int temp, int percent)> ReadCurve()
    {
        var list = new List<(int, int)>();
        for (int i = 0; i < 15; i++)
        {
            var r = GbWmi.Invoke(GbWmi.GetClass, "GetFanIndexValue", new Dictionary<string, object> { ["Index"] = i });
            list.Add((Convert.ToInt32(r["Temperture"]), DutyToPercent(Convert.ToInt32(r["Value"]))));
        }
        return list;
    }

    public static void WriteCurve(IReadOnlyList<(int temp, int percent)> curve)
    {
        for (int i = 0; i < curve.Count && i < 15; i++)
        {
            GbWmi.Invoke(GbWmi.SetClass, "SetFanIndexValue", new Dictionary<string, object>
            {
                ["Index"] = i, ["Temperture"] = curve[i].temp, ["Value"] = PercentToDuty(curve[i].percent),
            });
            System.Threading.Thread.Sleep(50); // GCC paces these at 50 ms
        }
    }

    public static int PercentToDuty(int p) => (int)Math.Round(Math.Max(0, Math.Min(100, p)) * FullDuty / 100.0);
    public static int DutyToPercent(int d) => (int)Math.Round(Math.Max(0, Math.Min(FullDuty, d)) * 100.0 / FullDuty);
}
