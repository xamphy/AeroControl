using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace AeroControl.Core;

/// <summary>
/// Start with Windows via a Task Scheduler logon task with highest privileges, so the elevated app starts
/// without a UAC prompt at every sign-in. Registered from XML so it also starts on battery and never times out
/// (schtasks /Create's defaults would skip sign-ins on battery and quoting breaks on "Program Files").
/// </summary>
public static class Startup
{
    private const string TaskName = "AeroControl";

    public static void Set(bool enabled)
    {
        try
        {
            if (!enabled) { Run($"/Delete /F /TN \"{TaskName}\""); return; }
            string exe = Process.GetCurrentProcess().MainModule.FileName;
            string user = WindowsIdentity.GetCurrent().Name;
            string xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>Aero Control (AERO 14 OLED controls)</Description></RegistrationInfo>
  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{SecurityElement.Escape(user)}</UserId></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>{SecurityElement.Escape(user)}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Priority>5</Priority>
  </Settings>
  <Actions Context=""Author""><Exec><Command>{SecurityElement.Escape(exe)}</Command><Arguments>--tray</Arguments></Exec></Actions>
</Task>";
            string file = Path.Combine(Path.GetTempPath(), "AeroControl.task.xml");
            File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
            Run($"/Create /F /TN \"{TaskName}\" /XML \"{file}\"");
            File.Delete(file);
        }
        catch (Exception ex) { Log.Write("Startup.Set: " + ex.Message); }
    }

    private static void Run(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
        {
            CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
        });
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        Log.Write($"Startup schtasks {args.Split(' ')[0]} -> {p.ExitCode} {output.Trim()}");
    }
}
