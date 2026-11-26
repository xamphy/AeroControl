using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using AeroControl.Display;
using AeroControl.Hardware;
using Microsoft.Win32;

namespace AeroControl.Core;

/// <summary>
/// Owns all hardware access. Every write runs on one background worker, in order, so the UI never blocks
/// and two settings never interleave on the EC. Sensors are polled every 2 s on the same worker.
/// </summary>
public sealed class Controller : IDisposable
{
    public static Controller Instance { get; private set; }

    public Settings Settings { get; }
    public SensorSnapshot Live { get; private set; } = new();
    public bool OnAc { get; private set; } = Sensors.OnAc;
    public bool? BatteryHealthy { get; private set; }
    public int BatteryDesignMwh { get; private set; } = -1;
    public int BatteryFullMwh { get; private set; } = -1;
    public int? BatteryRateMw { get; private set; }
    public int KeyboardLevel { get; private set; } = -1;

    public event Action LiveUpdated;
    public event Action StateChanged;

    private readonly BlockingCollection<Action> _queue = new();
    private readonly Thread _worker;
    private readonly Timer _poll;
    private readonly OledCare _oled = new();
    private KeyboardLight _kb;

    public Controller()
    {
        Instance = this;
        Settings = Settings.Load();
        _worker = new Thread(Work) { IsBackground = true, Name = "hw" };
        _worker.Start();
        _oled.PixelShiftEnabled = Settings.PixelShift;
        _oled.AscEnabled = Settings.AutoDim;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        Enqueue(() =>
        {
            BatteryHealthy = Battery.HealthNormal;
            (BatteryDesignMwh, BatteryFullMwh) = Battery.Capacity();
            Log.Write($"Battery capacity: design {BatteryDesignMwh} mWh, full {BatteryFullMwh} mWh");
            if (!App.ShotMode) try { _kb = KeyboardLight.Open(); KeyboardLevel = _kb?.GetBrightness() ?? -1; } catch { }
            if (Battery.ReadLimit() is ChargeLimit l) Settings.Charge = l;
            StateChanged?.Invoke();
        });
        _poll = new Timer(_ => Enqueue(Poll), null, 0, 2000);
    }

    public Mode CurrentMode => Modes.ById(OnAc ? Settings.ModeAc : Settings.ModeDc);

    /// <summary>The fan setting a mode really uses: its own profile, or the custom curve if switched on for it.</summary>
    public FanMode FanFor(Mode m) => Settings.UsesCurve(m.Index) ? FanMode.Custom : m.Fan;

    private System.Collections.Generic.List<(int, int)> CurveList => Settings.CurveTemps.Zip(Settings.CurvePercents, (t, p) => (t, p)).ToList();

    public void SetCustomCurve(bool on)
    {
        var m = CurrentMode;
        if (Settings.CustomCurve == null || Settings.CustomCurve.Length < 4) Settings.CustomCurve = new bool[4];
        Settings.CustomCurve[m.Index] = on;
        Settings.Save();
        StateChanged?.Invoke();
        var fan = FanFor(m);
        Enqueue(() => { Fans.Apply(fan, Settings.FixedFanPercent, CurveList); LogFans(); StateChanged?.Invoke(); });
    }

    private void LogFans()
    {
        var c = Fans.ReadMode() == FanMode.Custom ? " table=" + string.Join(" ", Fans.ReadCurve().Select(p => $"{p.temp}:{p.percent}")) : "";
        Log.Write($"  readback: fans={Fans.ReadMode()?.ToString() ?? "?"}{c}");
    }

    // ---------- actions (called from UI thread) ----------

    public void SelectMode(string id)
    {
        if (OnAc) Settings.ModeAc = id; else Settings.ModeDc = id;
        Settings.Save();
        StateChanged?.Invoke();
        var onAc = OnAc;
        Enqueue(() => ApplyMode(Modes.ById(id), onAc));
    }

    public void SetCharge(ChargeLimit l)
    {
        Settings.Charge = l; Settings.Save(); StateChanged?.Invoke();
        Enqueue(() =>
        {
            Battery.SetLimit(l); GccSync.WriteCharge(l);
            Log.Write($"  readback: charge={Battery.ReadLimit()?.ToString() ?? "?"} stop={(GbWmi.TryGet("GetChargeStop", out var s) ? s : -1)}");
        });
    }

    public void SetKeyboard(int percent, bool remember = true)
    {
        KeyboardLevel = percent;
        if (remember && Settings.KbFollowMode)
        {
            Settings.KbPerMode[CurrentMode.Index] = percent;
            Settings.Save();
        }
        Enqueue(() => { bool ok = _kb?.SetBrightness(percent) ?? false; Log.Write($"Keyboard slider -> {percent}% ok={ok}"); });
    }

    public void SetBrightness(int percent) => Enqueue(() => Panel.SetBrightness(percent));

    /// <summary>The keyboard changed its own level and reported it (Fn+Space): just record it, no HID traffic.</summary>
    public void KeyboardReported(int percent, Action done) => Enqueue(() => { KeyboardLevel = percent; done(); });

    /// <summary>Read the keyboard backlight on the worker (after the keyboard changed it itself) and report back.</summary>
    public void ReadKeyboard(Action<int> done) => Enqueue(() =>
    {
        int lvl = _kb?.GetBrightness() ?? -1;
        if (lvl >= 0) KeyboardLevel = lvl;
        done(lvl);
    });

    public void SetWhitePoint(int kelvin)
    {
        Settings.WhitePoint = kelvin; Settings.Save(); StateChanged?.Invoke();
        Enqueue(() => ColorTemperature.Apply(kelvin));
    }

    public void SetAutoRefresh(bool on)
    {
        Settings.AutoRefresh = on; Settings.Save();
        Enqueue(() => { GccSync.WritePanelRate(on); ApplyDisplay(OnAc); });
    }

    public void SetLowResOnBattery(bool on)
    {
        Settings.LowResOnBattery = on; Settings.Save();
        Enqueue(() => ApplyDisplay(OnAc));
    }

    public void SetPixelShift(bool on) { Settings.PixelShift = on; _oled.PixelShiftEnabled = on; Settings.Save(); GccSync.WriteOled(on, Settings.AutoDim); }
    public void SetAutoDim(bool on) { Settings.AutoDim = on; _oled.AscEnabled = on; Settings.Save(); GccSync.WriteOled(Settings.PixelShift, on); }
    public void SetKbAutoOff(bool on) => Enqueue(() => _kb?.SetAutoOff(on));

    public void SetFanCurve(int[] temps, int[] percents)
    {
        Settings.CurveTemps = temps; Settings.CurvePercents = percents; Settings.Save();
        var curve = CurveList;
        Enqueue(() => { Fans.WriteCurve(curve); Log.Write("Fan curve written"); LogFans(); });
    }

    // ---------- worker side ----------

    private void ApplyMode(Mode m, bool onAc)
    {
        Log.Write($"ApplyMode {m.Id} ({(onAc ? "AC" : "DC")})");
        Fans.Apply(FanFor(m), Settings.FixedFanPercent, CurveList);
        if (CpuPower.Available) CpuPower.ApplyStage(onAc ? m.CpuStageAc : m.CpuStageDc);
        WindowsPowerMode.Set(m.Overlay);
        GccSync.WriteMode(m, onAc);
        if (Settings.KbFollowMode && _kb != null)
        {
            int lvl = Settings.KbPerMode[m.Index];
            _kb.SetBrightness(lvl);
            KeyboardLevel = lvl;
        }
        // Read back what the hardware now reports, for verification
        Log.Write($"  readback: fans={Fans.ReadMode()?.ToString() ?? "?"} windows={WindowsPowerMode.Get()} kb={(_kb == null ? "none" : _kb.Tripped ? "disabled" : KeyboardLevel + "% (sent, not read back)")} pl={CpuPower.ReadLimits()}");
        StateChanged?.Invoke();
    }

    private void ApplyDisplay(bool onAc)
    {
        (int, int)? battery = Settings.LowResOnBattery ? (Settings.BatteryWidth, Settings.BatteryHeight) : ((int, int)?)null;
        Panel.ApplyPowerProfile(onAc, Settings.AutoRefresh, battery);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.StatusChange) return;
        bool ac = Sensors.OnAc;
        if (ac == OnAc) return;
        OnAc = ac;
        Log.Write($"Power source -> {(ac ? "AC" : "battery")}");
        StateChanged?.Invoke();
        Enqueue(() => { ApplyMode(CurrentMode, ac); ApplyDisplay(ac); });
    }

    /// <summary>Re-apply everything for the current power source (app start, resume).</summary>
    public void ApplyAll()
    {
        bool ac = OnAc;
        Enqueue(() =>
        {
            ApplyMode(CurrentMode, ac);
            ApplyDisplay(ac);
            if (Settings.WhitePoint != ColorTemperature.Native) ColorTemperature.Apply(Settings.WhitePoint);
        });
    }

    private void Poll()
    {
        Live = Sensors.Read();
        BatteryRateMw = Battery.RateMilliwatts();
        LiveUpdated?.Invoke();
    }

    /// <summary>What the hardware reports right now (read on the worker, after any queued writes). For --sweep logs.</summary>
    public System.Threading.Tasks.Task<string> DescribeAsync()
    {
        var t = new System.Threading.Tasks.TaskCompletionSource<string>();
        Enqueue(() =>
        {
            try
            {
                var s = Sensors.Read(); Live = s;
                t.SetResult($"fans={Fans.ReadMode()?.ToString() ?? "?"} duty={s.Fan1Pct}/{s.Fan2Pct}% rpm={s.Fan1Rpm}/{s.Fan2Rpm} " +
                            $"cpu={s.CpuTemp}C gpu={s.GpuTemp}C pl={CpuPower.ReadLimits()} windows={WindowsPowerMode.Get()} " +
                            $"kb={(_kb == null ? "none" : _kb.Tripped ? "DISABLED" : KeyboardLevel + "%")} " +
                            $"charge={Battery.ReadLimit()?.ToString() ?? "?"} battery={s.BatteryPercent}% ac={OnAc}");
            }
            catch (Exception ex) { t.SetResult("describe failed: " + ex.Message); }
        });
        return t.Task;
    }

    private void Enqueue(Action a) { if (!_queue.IsAddingCompleted) _queue.Add(a); }

    private void Work()
    {
        foreach (var a in _queue.GetConsumingEnumerable())
        {
            try { a(); }
            catch (Exception ex) { Log.Write("worker: " + ex); }
        }
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _poll.Dispose();
        _oled.Dispose();
        _queue.CompleteAdding();
        _worker.Join(2000);
        _kb?.Dispose();
    }
}
