using System;
using System.Runtime.InteropServices;

namespace AeroControl.Hardware;

/// <summary>
/// "Hibernate after" (SUB_SLEEP / HIBERNATEIDLE) of the active power plan: with Modern Standby the laptop drains its
/// battery while asleep, so after this long asleep it hibernates. GCC sets 900 s on battery for this model
/// (CPOWERAPI.SetS0ixS4Time) and leaves AC at never; we show and change the battery (DC) value. Seconds, 0 = never.
/// </summary>
public static class Hibernate
{
    private static Guid Sub = new("238c9fa8-0aad-41ed-83f4-97be242c8f20");
    private static Guid Setting = new("9d7815a6-7ee4-497e-8888-515a05f02364");

    public static int BatterySeconds
    {
        get
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return -1;
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(p);
                return PowerReadDCValueIndex(IntPtr.Zero, ref scheme, ref Sub, ref Setting, out uint v) == 0 ? (int)v : -1;
            }
            finally { LocalFree(p); }
        }
        set
        {
            if (PowerGetActiveScheme(IntPtr.Zero, out var p) != 0) return;
            try
            {
                var scheme = Marshal.PtrToStructure<Guid>(p);
                uint r = PowerWriteDCValueIndex(IntPtr.Zero, ref scheme, ref Sub, ref Setting, (uint)Math.Max(0, value));
                PowerSetActiveScheme(IntPtr.Zero, ref scheme); // make the change take effect
                Log.Write($"Hibernate after sleep on battery -> {value} s (result {r}), readback {BatterySeconds} s");
            }
            finally { LocalFree(p); }
        }
    }

    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerReadDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, out uint value);
    [DllImport("powrprof.dll")] private static extern uint PowerWriteDCValueIndex(IntPtr root, ref Guid scheme, ref Guid sub, ref Guid setting, uint value);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr p);
}
