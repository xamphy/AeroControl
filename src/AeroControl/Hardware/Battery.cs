using System;
using System.Management;

namespace AeroControl.Hardware;

public enum ChargeLimit { Full = 0, Balanced = 1, MaxHealth = 2 }

/// <summary>Charge limit, as GCC's GeneralVD does it: SetChargePolicy(0|4) + SetChargeStop(100|80|60).</summary>
public static class Battery
{
    public static int StopPercent(ChargeLimit l) => l switch { ChargeLimit.Balanced => 80, ChargeLimit.MaxHealth => 60, _ => 100 };

    public static void SetLimit(ChargeLimit l)
    {
        GbWmi.TrySet("SetChargePolicy", l == ChargeLimit.Full ? 0 : 4);
        GbWmi.TrySet("SetChargeStop", StopPercent(l));
        Log.Write($"Battery.SetLimit({l})");
    }

    /// <summary>Read back from the EC (policy reads 6 on this machine with a 60% stop, so trust the stop value).</summary>
    public static ChargeLimit? ReadLimit()
    {
        if (!GbWmi.TryGet("GetChargeStop", out var stop)) return null;
        if (GbWmi.TryGet("GetChargePolicy", out var pol) && pol == 0) return ChargeLimit.Full;
        return stop <= 60 ? ChargeLimit.MaxHealth : stop <= 80 ? ChargeLimit.Balanced : ChargeLimit.Full;
    }
    public static bool? HealthNormal => GbWmi.TryGet("GetBatteryHealth", out var h) ? h == 1 : (bool?)null;

    /// <summary>Design and full-charge capacity in mWh from Windows' battery class (root\wmi). -1 when unknown.</summary>
    public static (int design, int full) Capacity()
    {
        int design = ReadWmi("BatteryStaticData", "DesignedCapacity"), full = ReadWmi("BatteryFullChargedCapacity", "FullChargedCapacity");
        return (design, full);
    }

    /// <summary>Charge (+) or discharge (-) rate in mW (0 idle, null unknown) and the charge left in mWh (-1 unknown).</summary>
    public static (int? rateMw, int remainingMwh) Status()
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\wmi", "SELECT ChargeRate, DischargeRate, Charging, Discharging, RemainingCapacity FROM BatteryStatus");
            foreach (ManagementObject o in q.Get())
            {
                int rem = o["RemainingCapacity"] is null ? -1 : Convert.ToInt32(o["RemainingCapacity"]);
                if (o["Charging"] is bool c && c) return (Convert.ToInt32(o["ChargeRate"]), rem);
                if (o["Discharging"] is bool d && d) return (-Convert.ToInt32(o["DischargeRate"]), rem);
                return (0, rem);
            }
        }
        catch { }
        return (null, -1);
    }

    /// <summary>Windows' own estimate of battery time left (smoothed), null when not on battery or unknown.</summary>
    public static TimeSpan? WindowsTimeLeft
    {
        get
        {
            int s = System.Windows.Forms.SystemInformation.PowerStatus.BatteryLifeRemaining;
            return s > 0 ? TimeSpan.FromSeconds(s) : (TimeSpan?)null;
        }
    }

    private static int ReadWmi(string cls, string prop)
    {
        try
        {
            using var q = new ManagementObjectSearcher(@"root\wmi", $"SELECT {prop} FROM {cls}");
            foreach (ManagementObject o in q.Get()) return Convert.ToInt32(o[prop]);
        }
        catch (Exception ex) { Log.Write($"Battery {cls}: {ex.Message}"); }
        return -1;
    }
}
