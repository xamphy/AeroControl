using System;
using System.Runtime.InteropServices;

namespace AeroControl.Hardware;

public enum PowerOverlay { Efficiency, Balanced, Performance }

/// <summary>Windows 11 power mode slider (Settings > Power), the overlay GUIDs GCC's CPOWERAPI uses.</summary>
public static class WindowsPowerMode
{
    private static readonly Guid Saver = new("961cc777-2547-4f9d-8174-7d86181b8a7a");
    private static readonly Guid Balanced = Guid.Empty;
    private static readonly Guid Performance = new("ded574b5-45a0-4f42-8737-46345c09c238");

    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(ref Guid g);
    [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid g);

    public static void Set(PowerOverlay o)
    {
        var g = o switch { PowerOverlay.Efficiency => Saver, PowerOverlay.Performance => Performance, _ => Balanced };
        uint r = PowerSetActiveOverlayScheme(ref g);
        Log.Write($"WindowsPowerMode.Set({o}) -> {r}");
    }

    public static PowerOverlay Get()
    {
        if (PowerGetEffectiveOverlayScheme(out var g) != 0) return PowerOverlay.Balanced;
        return g == Saver ? PowerOverlay.Efficiency : g == Performance ? PowerOverlay.Performance : PowerOverlay.Balanced;
    }
}
