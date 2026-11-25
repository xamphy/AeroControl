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

    public static int Cycles => GbWmi.TryGet("getBattCyc1", out var c) ? c : -1;
    public static bool? HealthNormal => GbWmi.TryGet("GetBatteryHealth", out var h) ? h == 1 : (bool?)null;
}
