using System;
using Microsoft.Win32;

namespace AeroControl.Core;

/// <summary>
/// Keeps GIGABYTE Control Center's saved state (HKCU\SOFTWARE\GCC) in step with ours, so that GCC's
/// background parts re-apply the same choices instead of fighting them while both are installed.
/// </summary>
public static class GccSync
{
    private const string Key = @"SOFTWARE\GCC";

    public static void WriteMode(Mode m, bool onAc)
    {
        Try(k =>
        {
            k.SetValue(onAc ? "AiMode" : "AiModeDc", onAc ? m.GccAiModeAc : m.GccAiModeDc, RegistryValueKind.DWord);
            k.SetValue(onAc ? "FANMODE" : "FANMODEDC", m.GccFanMode, RegistryValueKind.DWord);
        });
    }

    public static void WriteCharge(Hardware.ChargeLimit c) => Try(k => k.SetValue("Charge Mode", (int)c, RegistryValueKind.DWord));

    public static void WritePanelRate(bool auto) => Try(k => k.SetValue("PanelRate", auto ? 1 : 0, RegistryValueKind.DWord));

    public static void WriteOled(bool pixelShift, bool asc) => Try(k =>
    {
        k.SetValue("PanelShift", pixelShift ? 1 : 0, RegistryValueKind.DWord);
        k.SetValue("ASC", asc ? 1 : 0, RegistryValueKind.DWord);
    });

    private static void Try(Action<RegistryKey> f)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(Key, writable: true);
            if (k != null) f(k);
        }
        catch (Exception ex) { Log.Write("GccSync: " + ex.Message); }
    }
}
