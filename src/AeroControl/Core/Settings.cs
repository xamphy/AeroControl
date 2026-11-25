using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using AeroControl.Hardware;

namespace AeroControl.Core;

[DataContract]
public sealed class Settings
{
    [DataMember] public string ModeAc = "balanced";
    [DataMember] public string ModeDc = "silent";
    [DataMember] public ChargeLimit Charge = ChargeLimit.MaxHealth;

    // Fans: a mode may override its default fan setting
    [DataMember] public int FixedFanPercent = 50;
    [DataMember] public int[] CurveTemps = { 0, 50, 53, 56, 59, 62, 65, 68, 71, 74, 77, 80, 83, 86, 89 };
    [DataMember] public int[] CurvePercents = { 25, 30, 35, 40, 45, 50, 55, 60, 65, 70, 75, 80, 85, 90, 100 };
    /// <summary>Per mode (silent, balanced, performance, turbo): use the custom curve instead of the mode's fan profile.</summary>
    [DataMember] public bool[] CustomCurve = new bool[4];

    /// <summary>Fans fully off below this temperature when the custom curve is in use (0 = never off).
    /// Verified 2026-11-26: the EC stops at 0% and restarts within ~1 s of crossing; it keeps running until ~6 °C cooler.</summary>
    [DataMember] public int FanOffBelow = 0;
    public const int SpinUpPercent = 35;

    /// <summary>The curve actually written to the EC: the user's curve with the fan-off zone applied.
    /// Points below the threshold go to 0%; the highest of them moves to the threshold so spin-up happens exactly there.</summary>
    public System.Collections.Generic.List<(int temp, int percent)> EffectiveCurve()
    {
        var c = new System.Collections.Generic.List<(int temp, int percent)>();
        for (int i = 0; i < CurveTemps.Length && i < CurvePercents.Length; i++) c.Add((CurveTemps[i], CurvePercents[i]));
        int x = FanOffBelow;
        if (x <= 0 || c.Count == 0) return c;
        int last = -1;
        for (int i = 0; i < c.Count; i++) if (c[i].temp < x) { last = i; c[i] = (c[i].temp, 0); }
        if (last >= 1 && (last + 1 >= c.Count || c[last + 1].temp > x))
            c[last] = (x, CurvePercents[last + 1 < c.Count ? last + 1 : last]);
        // spinning up from a standstill was verified at 35%: the first running point gets at least that
        for (int i = 0; i < c.Count; i++)
            if (c[i].percent > 0 || c[i].temp >= x) { if (c[i].percent < SpinUpPercent) c[i] = (c[i].temp, SpinUpPercent); }
        return c;
    }

    public bool UsesCurve(int modeIndex) => CustomCurve != null && modeIndex < CustomCurve.Length && CustomCurve[modeIndex];

    // Display
    [DataMember] public bool AutoRefresh = true;
    [DataMember] public bool LowResOnBattery = false;
    [DataMember] public int BatteryWidth = 1920, BatteryHeight = 1200;
    [DataMember] public int WhitePoint = 6500;

    // OLED care (Windows-side switches are read live from Windows, not stored)
    [DataMember] public bool PixelShift = true;
    [DataMember] public bool AutoDim = true;

    // Keyboard
    [DataMember] public bool KbFollowMode = true;
    [DataMember] public int[] KbPerMode = { 0, 50, 100, 100 }; // silent, balanced, performance, turbo

    // App
    [DataMember] public bool StartWithWindows = false; // the logon task is only created when you switch this on
    [DataMember] public bool DarkAppearance = true;
    [DataMember] public string Accent = "#0A84FF";

    private static string PathOf => System.IO.Path.Combine(Log.Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathOf))
                using (var fs = File.OpenRead(PathOf))
                    return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
        }
        catch (Exception ex) { Log.Write("Settings.Load: " + ex.Message); }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Log.Dir);
            using var fs = File.Create(PathOf);
            new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
        }
        catch (Exception ex) { Log.Write("Settings.Save: " + ex.Message); }
    }
}
