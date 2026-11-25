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
    [DataMember] public bool StartWithWindows = true;
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
