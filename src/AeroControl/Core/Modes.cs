using System.Collections.Generic;
using System.Linq;
using AeroControl.Hardware;

namespace AeroControl.Core;

/// <summary>
/// The four modes. Each maps onto what GCC's AI modes do on this laptop (GeneralVD.BtnAiClick / Execute_func):
///   Silent      = GCC "Power Saving" (AiMode 6): Eco fans, CPU stage "dc" 15/30 W, Windows best efficiency
///   Balanced    = GCC "Default" (0) / DC default (7): Normal fans, "two" 40/70 W ("dc2" 35/45 W on battery)
///   Performance = GCC "Gaming" (3): Power fans, "two" 40/70 W, balanced
///   Turbo       = GCC "Turbo" (2): Max fans, "five" 40/70 W, Windows best performance
/// On this i7-13620H, Performance and Turbo share the CPU limit; fans and Windows mode differ.
/// </summary>
public sealed class Mode
{
    public string Id, Name, Description, Glyph;
    public FanMode Fan;
    public string CpuStageAc, CpuStageDc;
    public string CpuSummaryAc, CpuSummaryDc;
    public PowerOverlay Overlay;
    public int GccAiModeAc, GccAiModeDc, GccFanMode;
    public int Index;
}

public static class Modes
{
    public static readonly IReadOnlyList<Mode> All = new[]
    {
        new Mode { Index = 0, Id = "silent", Name = "Silent", Description = "Quiet fans, 15 W CPU. Meetings, reading, battery.",
            Fan = FanMode.Eco, CpuStageAc = "dc", CpuStageDc = "dc", CpuSummaryAc = "15 W sustained · 30 W boost", CpuSummaryDc = "15 W sustained · 30 W boost",
            Overlay = PowerOverlay.Efficiency, GccAiModeAc = 6, GccAiModeDc = 9, GccFanMode = 2 },
        new Mode { Index = 1, Id = "balanced", Name = "Balanced", Description = "Normal fans, full CPU power when needed.",
            Fan = FanMode.Normal, CpuStageAc = "two", CpuStageDc = "dc2", CpuSummaryAc = "40 W sustained · 70 W boost", CpuSummaryDc = "35 W sustained · 45 W boost",
            Overlay = PowerOverlay.Balanced, GccAiModeAc = 0, GccAiModeDc = 7, GccFanMode = 3 },
        new Mode { Index = 2, Id = "performance", Name = "Performance", Description = "Fans ramp early to hold clocks under load.",
            Fan = FanMode.Power, CpuStageAc = "two", CpuStageDc = "two", CpuSummaryAc = "40 W sustained · 70 W boost", CpuSummaryDc = "40 W sustained · 70 W boost",
            Overlay = PowerOverlay.Balanced, GccAiModeAc = 3, GccAiModeDc = 3, GccFanMode = 1 },
        new Mode { Index = 3, Id = "turbo", Name = "Turbo", Description = "Fans at maximum. Rendering, gaming.",
            Fan = FanMode.Max, CpuStageAc = "five", CpuStageDc = "five", CpuSummaryAc = "40 W sustained · 70 W boost", CpuSummaryDc = "40 W sustained · 70 W boost",
            Overlay = PowerOverlay.Performance, GccAiModeAc = 2, GccAiModeDc = 2, GccFanMode = 4 },
    };

    public static Mode ById(string id) => All.FirstOrDefault(m => m.Id == id) ?? All[1];

    public static string FanLabel(FanMode f) => f switch
    {
        FanMode.Eco => "Eco", FanMode.Normal => "Normal", FanMode.Power => "Power", FanMode.Max => "Maximum",
        FanMode.Fixed => "Fixed", FanMode.Custom => "Custom curve", _ => f.ToString(),
    };

    public static string OverlayLabel(PowerOverlay o) => o switch
    {
        PowerOverlay.Efficiency => "Best power efficiency", PowerOverlay.Performance => "Best performance", _ => "Balanced",
    };
}
