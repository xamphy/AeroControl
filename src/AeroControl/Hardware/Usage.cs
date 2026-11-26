using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace AeroControl.Hardware;

/// <summary>
/// CPU, GPU and memory load, read the way Task Manager does (performance counters), so nothing wakes the RTX 4050:
/// nvidia-smi or NVML would pull it out of its low-power state on this Optimus laptop.
///  - CPU: "Processor Information / % Processor Utility / _Total" (Task Manager's number; can pass 100 with turbo, clamped)
///  - GPU: "GPU Engine / Utilization Percentage" for the NVIDIA adapter only (found once by LUID through DXGI):
///         summed per engine type over all processes, busiest engine type wins (as Task Manager shows it)
///  - RAM: GlobalMemoryStatusEx
/// The first counter read takes seconds, so Start() warms up on its own thread; until then values read -1.
/// </summary>
public static class Usage
{
    private static PerformanceCounter _cpu;
    private static PerformanceCounterCategory _gpuCat;
    private static string _gpuLuid;                       // "luid_0x00000000_0x00011a4c" (lower case)
    private static Dictionary<string, CounterSample> _prev = new();
    private static volatile bool _ready;

    public static string GpuName { get; private set; } = "";
    public static bool Ready => _ready;

    public static void Start() => new Thread(() =>
    {
        try
        {
            _cpu = new PerformanceCounter("Processor Information", "% Processor Utility", "_Total");
            _cpu.NextValue();
        }
        catch (Exception ex) { Log.Write("Usage CPU counter: " + ex.Message); _cpu = null; }
        try
        {
            (_gpuLuid, GpuName) = FindDiscreteGpu();
            if (_gpuLuid != null)
            {
                _gpuCat = new PerformanceCounterCategory("GPU Engine");
                SampleGpu(); // first read is slow; also seeds the previous samples
            }
            Log.Write($"Usage: gpu {(GpuName == "" ? "none" : GpuName)} {_gpuLuid}");
        }
        catch (Exception ex) { Log.Write("Usage GPU counters: " + ex.Message); _gpuCat = null; }
        _ready = true;
    }) { IsBackground = true, Name = "usage-init" }.Start();

    public static void Read(SensorSnapshot s)
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref m))
        {
            s.RamTotalMb = (int)(m.ullTotalPhys / (1024 * 1024));
            s.RamUsedMb = (int)((m.ullTotalPhys - m.ullAvailPhys) / (1024 * 1024));
        }
        if (!_ready) return;
        try { if (_cpu != null) s.CpuLoad = (int)Math.Round(Math.Min(100, _cpu.NextValue())); } catch { }
        try { if (_gpuCat != null) s.GpuLoad = SampleGpu(); } catch { }
    }

    private static int SampleGpu()
    {
        var data = _gpuCat.ReadCategory()["Utilization Percentage"];
        var now = new Dictionary<string, CounterSample>();
        var perType = new Dictionary<string, double>();
        foreach (InstanceData d in data.Values)
        {
            string name = d.InstanceName;
            if (name.IndexOf(_gpuLuid, StringComparison.OrdinalIgnoreCase) < 0) continue;
            now[name] = d.Sample;
            if (!_prev.TryGetValue(name, out var before)) continue;
            int at = name.LastIndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
            string type = at >= 0 ? name.Substring(at + 8) : "?";
            double v = CounterSample.Calculate(before, d.Sample);
            if (double.IsNaN(v) || v < 0) continue;
            perType[type] = (perType.TryGetValue(type, out var acc) ? acc : 0) + v;
        }
        _prev = now;
        return perType.Count == 0 ? 0 : (int)Math.Round(Math.Min(100, perType.Values.Max()));
    }

    // ---------- the NVIDIA adapter's LUID, through DXGI (read once) ----------

    private static (string luid, string name) FindDiscreteGpu()
    {
        var iid = typeof(IDXGIFactory1).GUID;
        if (CreateDXGIFactory1(ref iid, out var f) != 0) return (null, "");
        var factory = (IDXGIFactory1)f;
        (string, string, long) best = (null, "", -1);
        for (uint i = 0; factory.EnumAdapters1(i, out var a) == 0; i++)
        {
            a.GetDesc1(out var desc);
            Marshal.ReleaseComObject(a);
            bool software = (desc.Flags & 2) != 0; // DXGI_ADAPTER_FLAG_SOFTWARE
            long vram = (long)desc.DedicatedVideoMemory;
            if (software || vram <= best.Item3) continue;
            string luid = $"luid_0x{desc.AdapterLuidHigh:x8}_0x{desc.AdapterLuidLow:x8}";
            best = (luid, desc.Description.Trim(), vram);
        }
        Marshal.ReleaseComObject(factory);
        return (best.Item1, best.Item2);
    }

    [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object factory);

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();       // IDXGIObject
        void EnumAdapters(); void MakeWindowAssociation(); void GetWindowAssociation(); void CreateSwapChain(); void CreateSoftwareAdapter(); // IDXGIFactory
        [PreserveSig] int EnumAdapters1(uint index, out IDXGIAdapter1 adapter);                                // IDXGIFactory1
        [PreserveSig] bool IsCurrent();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        void SetPrivateData(); void SetPrivateDataInterface(); void GetPrivateData(); void GetParent();       // IDXGIObject
        void EnumOutputs(); void GetDesc(); void CheckInterfaceSupport();                                      // IDXGIAdapter
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);                                               // IDXGIAdapter1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public uint AdapterLuidLow; public int AdapterLuidHigh;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
