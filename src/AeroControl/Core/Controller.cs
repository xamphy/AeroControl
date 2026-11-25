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
            try { _kb = KeyboardLight.Open(); KeyboardLevel = _kb?.GetBrightness() ?? -1; } catch { }
            if (Battery.ReadLimit() is ChargeLimit l) Settings.Charge = l;
            StateChanged?.Invoke();
        });
        _poll = new Timer(_ => Enqueue(Poll), null, 0, 2000);
    }

    public Mode CurrentMode => Modes.ById(OnAc ? Settings.ModeAc : Settings.ModeDc);

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
        Enqueue(() => _kb?.SetBrightness(percent));
    }

    public void SetBrightness(int percent) => Enqueue(() => Panel.SetBrightness(percent));

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
        var curve = temps.Zip(percents, (t, p) => (t, p)).ToList();
        Enqueue(() => Fans.WriteCurve(curve));
    }

    // ---------- worker side ----------

    private void ApplyMode(Mode m, bool onAc)
    {
        Log.Write($"ApplyMode {m.Id} ({(onAc ? "AC" : "DC")})");
        Fans.Apply(m.Fan, Settings.FixedFanPercent);
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
        Log.Write($"  readback: fans={Fans.ReadMode()?.ToString() ?? "?"} windows={WindowsPowerMode.Get()} kb={_kb?.GetBrightness()?.ToString() ?? "?"} pl={CpuPower.ReadLimits()}");
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
        LiveUpdated?.Invoke();
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
