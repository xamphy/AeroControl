using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using AeroControl.Hardware;

namespace AeroControl;

/// <summary>
/// Read-only hardware survey: calls every GB_WMIACPI_Get method that takes no input
/// (plus the indexed fan-curve getter) and writes the results to a text file.
/// Never calls a setter.
/// </summary>
public static class Probe
{
    public static int Run(string outPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# AeroControl probe {DateTime.Now:s}");
        sb.AppendLine($"# elevated={Elevation.IsAdmin}");
        try
        {
            foreach (var m in GbWmi.Methods(GbWmi.GetClass).OrderBy(m => m.Name))
            {
                bool hasInput = m.InParameters != null && m.InParameters.Properties.Cast<PropertyData>().Any();
                if (hasInput)
                {
                    if (m.Name is "GetFanIndexValue")
                    {
                        for (int i = 0; i < 15; i++)
                            sb.AppendLine($"{m.Name}[{i}] = {Fmt(Safe(() => GbWmi.Invoke(GbWmi.GetClass, m.Name, new Dictionary<string, object> { ["Index"] = i })))}");
                    }
                    else sb.AppendLine($"{m.Name} = (skipped, needs input)");
                    continue;
                }
                sb.AppendLine($"{m.Name} = {Fmt(Safe(() => GbWmi.Invoke(GbWmi.GetClass, m.Name)))}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"FATAL {ex.GetType().Name}: {ex.Message}");
        }
        sb.AppendLine("# keyboard (HID 0414:7A3A, query commands only)");
        try
        {
            using var kb = KeyboardLight.Open();
            if (kb == null) sb.AppendLine("kb = not found");
            else
                foreach (byte cmd in new byte[] { 0x88, 0x89, 0x8A })
                {
                    var r = kb.Query(cmd);
                    sb.AppendLine($"kb query 0x{cmd:X2} = {(r == null ? "failed" : BitConverter.ToString(r))}");
                }
        }
        catch (Exception ex) { sb.AppendLine($"kb ERR {ex.Message}"); }
        sb.AppendLine("# display / OLED care (read-only)");
        try
        {
            sb.AppendLine($"panel device = {Display.Panel.DeviceName}");
            sb.AppendLine($"refresh current = {Display.Panel.CurrentRefreshRate} Hz, available = {string.Join("/", Display.Panel.AvailableRefreshRates)}");
            sb.AppendLine($"brightness = {Display.Panel.GetBrightness()}");
            sb.AppendLine($"dark mode = {Display.OledCare.DarkMode}, transparency = {Display.OledCare.TaskbarTransparency}, taskbar autohide = {Display.OledCare.TaskbarAutoHide}");
        }
        catch (Exception ex) { sb.AppendLine($"display ERR {ex.Message}"); }
        File.WriteAllText(outPath, sb.ToString());
        return 0;
    }

    private static object Safe(Func<Dictionary<string, object>> f)
    {
        try { return f(); }
        catch (Exception ex) { return "ERR " + ex.Message.Trim(); }
    }

    private static string Fmt(object o) => o is Dictionary<string, object> d
        ? string.Join(", ", d.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"))
        : o?.ToString();
}
