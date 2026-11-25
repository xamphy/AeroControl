using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AeroControl.Hardware;

/// <summary>
/// CPU power limits (PL1/PL2/Tau). Delegates to GCC's own CPUOC.dll, which reads the per-model table
/// (P4EBVF for this laptop: "two"/"five" = 40/70 W, "dc2" = 35/45 W, "dc" = 15/30 W) and writes the
/// limits through GIGABYTE's YccDrvv3 driver. We do not reimplement the MSR/MMIO writes ourselves.
/// </summary>
public static class CpuPower
{
    public const string GccRoot = @"C:\Program Files\GIGABYTE\Control Center";
    public static readonly string NotebookDir = Path.Combine(GccRoot, @"Lib\GBT_Notebook");

    private static bool _init;
    private static Type _cpuType;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetDllDirectory(string path);

    public static bool Available => File.Exists(Path.Combine(NotebookDir, "CPUOC.dll"));

    private static void Init()
    {
        if (_init) return;
        _init = true;
        SetDllDirectory(NotebookDir); // YccDrvv3.dll
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name + ".dll";
            foreach (var dir in new[] { NotebookDir, GccRoot })
            {
                var p = Path.Combine(dir, name);
                if (File.Exists(p)) return Assembly.LoadFrom(p);
            }
            return null;
        };
        var cpuoc = Assembly.LoadFrom(Path.Combine(NotebookDir, "CPUOC.dll"));
        _cpuType = cpuoc.GetType("CPUOC.CPU", throwOnError: true);

        // CPUOC reads these statics from ComData (ComHeader.ComData)
        var comData = Assembly.LoadFrom(Path.Combine(NotebookDir, "ComData.dll")).GetType("ComHeader.ComData", true);
        comData.GetField("CpuType").SetValue(null, CpuFamily());
        comData.GetField("sBiosModelName").SetValue(null, Model());
    }

    /// <summary>Apply a stage from the model table: "dc", "dc2", "two", "five" (see class summary).</summary>
    public static bool ApplyStage(string stage)
    {
        try
        {
            Init();
            object cpu = Activator.CreateInstance(_cpuType);
            var args = new object[] { stage };
            _cpuType.GetMethod("OC").Invoke(cpu, args);
            Log.Write($"CpuPower.ApplyStage({stage}) ok");
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"CpuPower.ApplyStage({stage}) failed: {(ex.InnerException ?? ex).Message}");
            return false;
        }
    }

    public static string Model()
    {
        foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem").Get())
            return o["Model"]?.ToString().Trim();
        return null;
    }

    private static string CpuFamily()
    {
        foreach (System.Management.ManagementObject o in new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor").Get())
        {
            var n = o["Name"]?.ToString() ?? "";
            if (n.Contains("i9") || n.Contains("Ultra 9")) return "i9";
            if (n.Contains("i5") || n.Contains("Ultra 5")) return "i5";
            return "i7";
        }
        return "i7";
    }
}
