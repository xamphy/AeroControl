using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using AeroControl.Core;
using AeroControl.Display;
using AeroControl.Hardware;

namespace AeroControl.ViewModels;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    protected void Raise([CallerMemberName] string n = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    protected void RaiseAll() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
}

public sealed class ModeItem : Observable
{
    private readonly MainViewModel _vm;
    public Mode Mode { get; }
    public ModeItem(MainViewModel vm, Mode m) { _vm = vm; Mode = m; }
    public string Name => Mode.Name;
    public string Description => Mode.Description;
    /// <summary>Icon path (24×24 stroke), same artwork as the approved mockup.</summary>
    public string Glyph => Mode.Index switch
    {
        0 => "M11 5L6 9H3v6h3l5 4zM22 9l-6 6M16 9l6 6",
        1 => "M12 3v18M5 8h14M7 8l-3 7h6zM17 8l-3 7h6z",
        2 => "M13 2L4 14h7l-1 8 9-12h-7z",
        _ => "M12 2c3 4 5 6 5 10a5 5 0 0 1-10 0c0-2 1-3 2-4 0 2 1 3 2 3 0-3 1-6 1-9z",
    };
    public string Short
    {
        get
        {
            var fan = _vm.Controller.FanFor(Mode);
            string f = fan == FanMode.Custom ? "Custom" : fan == FanMode.Max ? "Max" : Modes.FanLabel(fan);
            return $"{f} · {(Mode.CpuStageAc == "dc" ? "15" : "40")} W";
        }
    }
    public bool IsSelected
    {
        get => _vm.Controller.CurrentMode.Id == Mode.Id;
        set { if (value) _vm.Controller.SelectMode(Mode.Id); }
    }
    public void Refresh() { Raise(nameof(IsSelected)); Raise(nameof(Short)); }
}

public sealed class InfoRow { public string Label { get; set; } public string Value { get; set; } }

public sealed class CurvePoint : Observable
{
    private int _temp, _percent;
    public int Temp { get => _temp; set { _temp = value; Raise(); } }
    public int Percent { get => _percent; set { _percent = value; Raise(); } }
}

public sealed class MainViewModel : Observable
{
    public Controller Controller { get; }
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _brightDebounce, _kbDebounce;

    public ObservableCollection<ModeItem> ModeItems { get; }
    public ObservableCollection<CurvePoint> Curve { get; }

    public MainViewModel(Controller c)
    {
        Controller = c;
        ModeItems = new ObservableCollection<ModeItem>(Modes.All.Select(m => new ModeItem(this, m)));
        var s = c.Settings;
        Curve = new ObservableCollection<CurvePoint>(s.CurveTemps.Zip(s.CurvePercents, (t, p) => new CurvePoint { Temp = t, Percent = p }));
        _brightness = Panel.GetBrightness();
        _brightDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _brightDebounce.Tick += (_, _) => { _brightDebounce.Stop(); Controller.SetBrightness(_brightness); };
        _kbDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(90) };
        _kbDebounce.Tick += (_, _) => { _kbDebounce.Stop(); Controller.SetKeyboard(_kb); };
        _kb = c.KeyboardLevel >= 0 ? c.KeyboardLevel : 100;

        c.LiveUpdated += () => _ui.BeginInvoke(new Action(OnLive));
        c.StateChanged += () => _ui.BeginInvoke(new Action(OnState));
    }

    private void OnLive()
    {
        Raise(nameof(CpuTemp)); Raise(nameof(GpuTemp)); Raise(nameof(Fan1)); Raise(nameof(Fan2));
        Raise(nameof(Fan1Duty)); Raise(nameof(Fan2Duty)); Raise(nameof(CpuPct)); Raise(nameof(GpuPct));
        Raise(nameof(Fan1Pct)); Raise(nameof(Fan2Pct)); Raise(nameof(BatteryText)); Raise(nameof(PowerSource));
        Raise(nameof(BatteryPercentText)); Raise(nameof(CpuHeat)); Raise(nameof(GpuHeat)); Raise(nameof(CpuTempValue));
        Raise(nameof(BatteryRateText));
    }

    /// <summary>Re-read values that can change outside the app (Fn keys): brightness, keyboard level.</summary>
    public void RefreshFromHardware()
    {
        int b = Panel.GetBrightness();
        if (b >= 0 && !_brightDebounce.IsEnabled) { _brightness = b; Raise(nameof(Brightness)); }
        if (Controller.KeyboardLevel >= 0 && !_kbDebounce.IsEnabled)
        {
            _kb = Controller.KeyboardLevel;
            Raise(nameof(Keyboard)); Raise(nameof(KeyboardText)); Raise(nameof(KeyGlow));
        }
    }

    private void OnState()
    {
        foreach (var m in ModeItems) m.Refresh();
        if (Controller.KeyboardLevel >= 0 && !_kbDebounce.IsEnabled) _kb = Controller.KeyboardLevel;
        RaiseAll();
    }

    // ---------- navigation ----------
    private string _page = "home";
    public string Page { get => _page; set { _page = value; RaiseAll(); } }
    public string PageTitle => _page switch { "perf" => "Performance", "battery" => "Battery", "display" => "Display", "keyboard" => "Keyboard", "about" => "About", "settings" => "Settings", _ => "Home" };
    public bool IsHome { get => _page == "home"; set { if (value) Page = "home"; } }
    public bool IsPerf { get => _page == "perf"; set { if (value) Page = "perf"; } }
    public bool IsBattery { get => _page == "battery"; set { if (value) Page = "battery"; } }
    public bool IsDisplay { get => _page == "display"; set { if (value) Page = "display"; } }
    public bool IsKeyboard { get => _page == "keyboard"; set { if (value) Page = "keyboard"; } }
    public bool IsAbout { get => _page == "about"; set { if (value) Page = "about"; } }
    public bool IsSettings { get => _page == "settings"; set { if (value) Page = "settings"; } }

    // ---------- live ----------
    private SensorSnapshot L => Controller.Live;
    public string CpuTemp => L.CpuTemp >= 0 ? L.CpuTemp.ToString() : "–";
    public string GpuTemp => L.GpuTemp > 0 ? L.GpuTemp.ToString() : "–";
    public string Fan1 => L.Fan1Rpm >= 0 ? L.Fan1Rpm.ToString("N0") : "–";
    public string Fan2 => L.Fan2Rpm >= 0 ? L.Fan2Rpm.ToString("N0") : "–";
    public string Fan1Duty => L.Fan1Pct >= 0 ? $"{L.Fan1Pct}% duty" : "";
    public string Fan2Duty => L.Fan2Pct >= 0 ? $"{L.Fan2Pct}% duty" : "";
    public double CpuPct => Math.Max(0, L.CpuTemp) / 100.0;
    public double GpuPct => Math.Max(0, L.GpuTemp) / 100.0;
    /// <summary>"" normal, "warm", "hot" — colours the temperature bars (CPU throttles at 100 °C, the RTX 4050 Laptop near 87 °C).</summary>
    public string CpuHeat => L.CpuTemp >= 95 ? "hot" : L.CpuTemp >= 85 ? "warm" : "";
    public string GpuHeat => L.GpuTemp >= 85 ? "hot" : L.GpuTemp >= 78 ? "warm" : "";
    public double CpuTempValue => L.CpuTemp;
    public double Fan1Pct => Math.Max(0, L.Fan1Pct) / 100.0;
    public double Fan2Pct => Math.Max(0, L.Fan2Pct) / 100.0;
    public string PowerSource => Controller.OnAc ? "plugged in" : "on battery";
    public string BatteryText
    {
        get
        {
            string pct = L.BatteryPercent >= 0 ? $"{L.BatteryPercent}%" : "";
            if (!Controller.OnAc) return $"On battery · {pct}";
            return $"Plugged in · {pct}{(Controller.Settings.Charge == ChargeLimit.Full ? "" : $" · limit {Battery.StopPercent(Controller.Settings.Charge)}%")}";
        }
    }

    // ---------- mode ----------
    public Mode Current => Controller.CurrentMode;
    public string ModeName => Current.Name;
    private string FanName => Controller.FanFor(Current) == FanMode.Custom ? "Custom curve" : Modes.FanLabel(Current.Fan);
    public string ModeDetail => $"{(Controller.FanFor(Current) == FanMode.Custom ? "Custom fan curve" : Modes.FanLabel(Current.Fan) + " fans")} · {(Controller.OnAc ? Current.CpuSummaryAc : Current.CpuSummaryDc)}";
    public string ModeCaption => $"Current mode · {PowerSource}";
    public IEnumerable<InfoRow> ModeRows => new[]
    {
        new InfoRow { Label = "Fans", Value = FanName },
        new InfoRow { Label = "CPU power limit", Value = Controller.OnAc ? Current.CpuSummaryAc : Current.CpuSummaryDc },
        new InfoRow { Label = "Windows power mode", Value = Modes.OverlayLabel(Current.Overlay) },
        new InfoRow { Label = "Keyboard backlight", Value = Controller.Settings.KbFollowMode ? $"{Controller.Settings.KbPerMode[Current.Index]}%" : "Unchanged" },
    };

    // ---------- battery ----------
    public bool ChargeFull { get => Controller.Settings.Charge == ChargeLimit.Full; set { if (value) Controller.SetCharge(ChargeLimit.Full); } }
    public bool ChargeBalanced { get => Controller.Settings.Charge == ChargeLimit.Balanced; set { if (value) Controller.SetCharge(ChargeLimit.Balanced); } }
    public bool ChargeHealth { get => Controller.Settings.Charge == ChargeLimit.MaxHealth; set { if (value) Controller.SetCharge(ChargeLimit.MaxHealth); } }
    public string ChargeLimitText => $"{Battery.StopPercent(Controller.Settings.Charge)}%";
    public string ChargeName => Controller.Settings.Charge switch { ChargeLimit.Full => "Full capacity", ChargeLimit.Balanced => "Balanced", _ => "Max battery health" };
    public void CycleCharge() => Controller.SetCharge((ChargeLimit)(((int)Controller.Settings.Charge + 1) % 3));
    public string BatteryPercentText => L.BatteryPercent >= 0 ? $"{L.BatteryPercent}%" : "–";
    public string FanText => FanName;
    public string Health => Controller.BatteryHealthy switch { true => "Normal", false => "Service recommended", _ => "–" };
    private static string Wh(int mwh) => (mwh / 1000.0).ToString("0.0") + " Wh";
    public bool HasCapacity => Controller.BatteryDesignMwh > 0 && Controller.BatteryFullMwh > 0;
    public string CapacityText => HasCapacity ? $"{Wh(Controller.BatteryFullMwh)} of {Wh(Controller.BatteryDesignMwh)}" : "–";
    public double CapacityPct => HasCapacity ? Math.Min(1.0, (double)Controller.BatteryFullMwh / Controller.BatteryDesignMwh) : 0;
    public string CapacityPctText => HasCapacity ? $"{Math.Min(100, Math.Round(100.0 * Controller.BatteryFullMwh / Controller.BatteryDesignMwh)):0}%" : "–";
    public string WearText => HasCapacity ? $"{Math.Max(0, Math.Round(100 - 100.0 * Controller.BatteryFullMwh / Controller.BatteryDesignMwh)):0}%" : "–";
    public string BatteryRateText => Controller.BatteryRateMw switch
    {
        null => "–",
        0 => "Idle",
        > 0 and var r => $"Charging · {r / 1000.0:0.0} W",
        var r => $"Using {-r / 1000.0:0.0} W",
    };

    // ---------- display ----------
    private int _brightness;
    public int Brightness { get => _brightness; set { _brightness = value; Raise(); _brightDebounce.Stop(); _brightDebounce.Start(); } }
    public bool AutoRefresh { get => Controller.Settings.AutoRefresh; set { Controller.SetAutoRefresh(value); Raise(); } }
    public bool LowRes { get => Controller.Settings.LowResOnBattery; set { Controller.SetLowResOnBattery(value); Raise(); } }
    public string ResolutionText { get { var (w, h) = Panel.CurrentResolution; return $"{w} × {h}"; } }
    public string RefreshText => $"{Panel.CurrentRefreshRate} Hz";
    public string BatteryResolutionText => $"{Controller.Settings.BatteryWidth} × {Controller.Settings.BatteryHeight} when unplugged. Text looks softer.";
    public bool White5800 { get => Controller.Settings.WhitePoint == 5800; set { if (value) Controller.SetWhitePoint(5800); } }
    public bool White6000 { get => Controller.Settings.WhitePoint == 6000; set { if (value) Controller.SetWhitePoint(6000); } }
    public bool White6500 { get => Controller.Settings.WhitePoint == 6500; set { if (value) Controller.SetWhitePoint(6500); } }
    public bool White6800 { get => Controller.Settings.WhitePoint == 6800; set { if (value) Controller.SetWhitePoint(6800); } }
    public string WhiteText => $"{Controller.Settings.WhitePoint}K";
    public bool WarmWhite { get => Controller.Settings.WhitePoint < 6500; set => Controller.SetWhitePoint(value ? 5800 : 6500); }

    public bool PixelShift { get => Controller.Settings.PixelShift; set { Controller.SetPixelShift(value); Raise(); } }
    public bool AutoDim { get => Controller.Settings.AutoDim; set { Controller.SetAutoDim(value); Raise(); } }
    public bool DarkMode { get => OledCare.DarkMode; set { OledCare.DarkMode = value; Raise(); } }
    public bool AutoHideTaskbar { get => OledCare.TaskbarAutoHide; set { OledCare.TaskbarAutoHide = value; Raise(); } }
    public bool Transparency { get => OledCare.TaskbarTransparency; set { OledCare.TaskbarTransparency = value; Raise(); } }

    // ---------- keyboard ----------
    private int _kb;
    public int Keyboard { get => _kb; set { _kb = value - value % 2; Raise(); Raise(nameof(KeyboardText)); Raise(nameof(KeyGlow)); _kbDebounce.Stop(); _kbDebounce.Start(); } }
    public string KeyboardText => $"{_kb}%";
    public double KeyGlow => _kb / 100.0;
    private bool _kbAutoOff;
    public bool KbAutoOff { get => _kbAutoOff; set { _kbAutoOff = value; Controller.SetKbAutoOff(value); Raise(); } }
    public bool KbFollow { get => Controller.Settings.KbFollowMode; set { Controller.Settings.KbFollowMode = value; Controller.Settings.Save(); Raise(); } }

    // ---------- fan curve ----------
    public bool CustomCurve { get => Controller.Settings.UsesCurve(Current.Index); set => Controller.SetCustomCurve(value); }
    public string CustomCurveHint => $"Replaces {Current.Name}'s fan profile with the curve below. Other modes keep their own.";
    public void CommitCurve() => Controller.SetFanCurve(Curve.Select(p => p.Temp).ToArray(), Curve.Select(p => p.Percent).ToArray());

    // ---------- app settings ----------
    public bool DarkAppearance
    {
        get => Controller.Settings.DarkAppearance;
        set { Controller.Settings.DarkAppearance = value; Controller.Settings.Save(); Themes.ThemeManager.Apply(value, Controller.Settings.Accent); Raise(); }
    }
    public bool StartWithWindows
    {
        get => Controller.Settings.StartWithWindows;
        set { Controller.Settings.StartWithWindows = value; Controller.Settings.Save(); Startup.Set(value); Raise(); }
    }
    public void SetAccent(string hex) { Controller.Settings.Accent = hex; Controller.Settings.Save(); Themes.ThemeManager.Apply(DarkAppearance, hex); }

    // ---------- about ----------
    public IEnumerable<InfoRow> About => new[]
    {
        new InfoRow { Label = "Model", Value = "AERO 14 OLED BMF" },
        new InfoRow { Label = "Processor", Value = "Intel Core i7-13620H" },
        new InfoRow { Label = "Graphics", Value = "NVIDIA GeForce RTX 4050 Laptop" },
        new InfoRow { Label = "Display", Value = "14″ OLED · 2880 × 1800 · 90 Hz" },
        new InfoRow { Label = "Battery", Value = Health },
        new InfoRow { Label = "Aero Control", Value = "0.1" },
    };
}
