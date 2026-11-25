using System;
using System.Diagnostics;

namespace AeroControl.Core;

/// <summary>
/// Start with Windows via a Task Scheduler logon task with highest privileges, so the elevated app
/// starts without a UAC prompt at every boot (the same mechanism GCC's own "GCC" task uses).
/// </summary>
public static class Startup
{
    private const string TaskName = "AeroControl";

    public static void Set(bool enabled)
    {
        string exe = Process.GetCurrentProcess().MainModule.FileName;
        string args = enabled
            ? $"/Create /F /TN \"{TaskName}\" /SC ONLOGON /RL HIGHEST /TR \"\\\"{exe}\\\" --tray\""
            : $"/Delete /F /TN \"{TaskName}\"";
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            });
            p.WaitForExit(10000);
            Log.Write($"Startup.Set({enabled}) -> exit {p.ExitCode}");
        }
        catch (Exception ex) { Log.Write("Startup.Set: " + ex.Message); }
    }
}
