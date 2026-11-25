using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using AeroControl.Core;
using AeroControl.ViewModels;
using AeroControl.Views;
using Microsoft.Win32;

namespace AeroControl;

public partial class App : Application
{
    public static bool ShotMode { get; private set; }
    private Mutex _single;
    private EventWaitHandle _showSignal;
    private Controller _controller;
    private MainViewModel _vm;
    private MainWindow _main;
    private PopoverWindow _popover;
    private TrayIcon _tray;
    private FnKeys _fnKeys;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write("FATAL " + e.ExceptionObject);
        DispatcherUnhandledException += (_, e) => { Log.Write("UI exception " + e.Exception); e.Handled = true; };
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;

        // ---- diagnostic command lines (no UI) ----
        int probe = Array.IndexOf(args, "--probe");
        if (probe >= 0)
        {
            Shutdown(Probe.Run(probe + 1 < args.Length ? args[probe + 1] : "probe.txt"));
            return;
        }
        int su = Array.IndexOf(args, "--startup"); // --startup on|off: register/remove the logon task (installer)
        if (su >= 0 && su + 1 < args.Length)
        {
            bool on = args[su + 1] == "on";
            var st = Core.Settings.Load(); st.StartWithWindows = on; st.Save();
            Core.Startup.Set(on);
            Shutdown(0); return;
        }
        if (args.Contains("--kbtest")) { Shutdown(KbTest()); return; }
        if (args.Contains("--kbsweep")) { Shutdown(KbSweep(8000)); return; }
        if (args.Contains("--curvetest")) { Shutdown(CurveTest()); return; }
        if (args.Contains("--fanofftest")) { Shutdown(FanOffTest()); return; }

        // ---- single instance: a second launch just shows the running one ----
        // (--shot renders alongside a running instance and never touches the keyboard or Fn keys)
        ShotMode = args.Contains("--shot");
        _single = new Mutex(!ShotMode, "AeroControl.Single", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShotMode ? null : "AeroControl.Show");
        if (!first && !ShotMode) { _showSignal.Set(); Shutdown(0); return; }

        bool firstRun = !File.Exists(Path.Combine(Log.Dir, "settings.json"));
        _controller = new Controller();
        if (firstRun) ImportFromGcc(_controller.Settings);
        Themes.ThemeManager.Apply(_controller.Settings.DarkAppearance, _controller.Settings.Accent);

        _vm = new MainViewModel(_controller);
        _main = new MainWindow(_vm);
        _popover = new PopoverWindow(_vm, page => _main.ShowAndActivate(page));
        _tray = new TrayIcon(() => _popover.Toggle(), () => _main.ShowAndActivate(), Quit);
        if (!ShotMode) try { _fnKeys = new FnKeys(_controller, Dispatcher, () => _vm.RefreshFromHardware()); }
        catch (Exception ex) { Log.Write("FnKeys init failed: " + ex); }

        var t = new Thread(() => { while (_showSignal.WaitOne()) Dispatcher.BeginInvoke(new Action(() => _main.ShowAndActivate())); })
        { IsBackground = true };
        t.Start();

        int shot = Array.IndexOf(args, "--shot");
        if (shot >= 0) { Snapshots(shot + 1 < args.Length ? args[shot + 1] : Log.Dir); return; }

        bool fromLogon = args.Contains("--tray");
        if (fromLogon) _controller.ApplyAll(); // at sign-in, restore everything (as GCC does at boot)
        else _main.ShowAndActivate();          // a manual launch only shows; nothing is changed until you choose
        Log.Write($"started (tray={fromLogon}, firstRun={firstRun})");
    }

    /// <summary>On first run, start from what GCC currently has, so opening the app changes nothing.</summary>
    private static void ImportFromGcc(Settings s)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\GCC");
            if (k == null) return;
            string FromAc(int v) => v switch { 6 or 5 or 4 => "silent", 3 or 1 => "performance", 2 => "turbo", _ => "balanced" };
            string FromDc(int v) => v switch { 9 or 8 or 4 or 6 => "silent", 3 or 1 => "performance", 2 => "turbo", _ => "balanced" };
            s.ModeAc = FromAc(Convert.ToInt32(k.GetValue("AiMode", 0)));
            s.ModeDc = FromDc(Convert.ToInt32(k.GetValue("AiModeDc", 7)));
            s.AutoRefresh = Convert.ToInt32(k.GetValue("PanelRate", 1)) == 1;
            s.PixelShift = Convert.ToInt32(k.GetValue("PanelShift", 0)) == 1;
            s.AutoDim = Convert.ToInt32(k.GetValue("ASC", 0)) == 1;
            // Per-mode keyboard brightness from GCC's AI-mode keys ("effect_brightnessHex_panel_gpu")
            int Kb(string key, int fallback)
            {
                var parts = (k.GetValue(key) as string)?.Split('_');
                return parts != null && parts.Length >= 2 && int.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out var v)
                    ? Math.Max(0, Math.Min(100, v)) : fallback;
            }
            s.KbPerMode = new[] { Kb("Skey", 100), Kb("Dkey", 100), Kb("Gkey", 100), Kb("Tkey", 100) };
            int cm = Convert.ToInt32(k.GetValue("Charge Mode", 0));
            s.Charge = (Hardware.ChargeLimit)Math.Max(0, Math.Min(2, cm));
            s.Save();
            Log.Write($"Imported from GCC: AC={s.ModeAc} DC={s.ModeDc}");
        }
        catch (Exception ex) { Log.Write("ImportFromGcc: " + ex.Message); }
    }

    private void Quit()
    {
        _fnKeys?.Dispose();
        _tray?.Dispose();
        _controller?.Dispose();
        Shutdown(0);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Render every page of the main window and the popover to PNGs (read-only; for design review).</summary>
    private async void Snapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        _main.Show();
        await System.Threading.Tasks.Task.Delay(2500); // first sensor poll
        foreach (var page in new[] { "home", "perf", "battery", "display", "keyboard", "about", "settings" })
        {
            _vm.Page = page;
            await System.Threading.Tasks.Task.Delay(400);
            Save(_main, Path.Combine(dir, $"main-{page}.png"));
        }
        // fan curve editor, shown in memory only (nothing saved or sent to the fans)
        var st = _controller.Settings;
        var saved = st.CustomCurve;
        st.CustomCurve = new[] { true, true, true, true };
        int savedOff = st.FanOffBelow; st.FanOffBelow = 50;
        _vm.Page = "perf";
        await System.Threading.Tasks.Task.Delay(400);
        FindScroll(_main)?.ScrollToEnd();
        await System.Threading.Tasks.Task.Delay(300);
        Save(_main, Path.Combine(dir, "main-perf-curve.png"));
        FindScroll(_main)?.ScrollToHome();
        st.CustomCurve = saved; st.FanOffBelow = savedOff;
        _popover.Toggle();
        await System.Threading.Tasks.Task.Delay(600);
        Save(_popover, Path.Combine(dir, "popover.png"));
        var osd = new Views.OsdWindow();
        osd.ShowLevel("M4 9h4l5-4v14l-5-4H4zM16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11", "Volume", 48);
        await System.Threading.Tasks.Task.Delay(600);
        osd.ShowLevel("M4 9h4l5-4v14l-5-4H4zM16 9a4 4 0 0 1 0 6M18.5 6.5a8 8 0 0 1 0 11", "Volume", 48);
        await System.Threading.Tasks.Task.Delay(300);
        Save(osd, Path.Combine(dir, "osd-level.png"));
        osd.ShowState("M4 5h16v14H4zM4 15h16M12 15v4", "Touchpad", "Off");
        await System.Threading.Tasks.Task.Delay(400);
        Save(osd, Path.Combine(dir, "osd-state.png"));
        Quit();

        static System.Windows.Controls.ScrollViewer FindScroll(DependencyObject o)
        {
            if (o is System.Windows.Controls.ScrollViewer sv) return sv;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
                if (FindScroll(System.Windows.Media.VisualTreeHelper.GetChild(o, i)) is { } f) return f;
            return null;
        }

        static void Save(Window w, string file)
        {
            w.UpdateLayout();
            var root = (FrameworkElement)w.Content;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            double fw = root.ActualWidth + root.Margin.Left + root.Margin.Right, fh = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)(fw * dpi.DpiScaleX), (int)(fh * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                System.Windows.Media.PixelFormats.Pbgra32);
            if (w.Background is System.Windows.Media.SolidColorBrush b && b.Color.A > 0) // transparent windows stay transparent
            {
                var bg = new System.Windows.Media.DrawingVisual();
                using (var dc = bg.RenderOpen()) dc.DrawRectangle(b, null, new Rect(0, 0, fw, fh));
                rtb.Render(bg);
            }
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(file);
            enc.Save(fs);
        }
    }

    // ---------------- diagnostics kept from bring-up ----------------

    /// <summary>
    /// Custom fan curve on real hardware: flat 60% curve, then flat 30%, logging the EC's fan duty and RPM each second,
    /// then the current mode's own fan profile and the saved curve are put back. Log in aerocontrol.log.
    /// </summary>
    private static int CurveTest()
    {
        var st = Core.Settings.Load();
        var mode = Modes.ById(Hardware.Sensors.OnAc ? st.ModeAc : st.ModeDc);
        var saved = st.CurveTemps.Zip(st.CurvePercents, (t, p) => (t, p)).ToList();
        Log.Write($"CURVETEST start: mode {mode.Id}, fans={Hardware.Fans.ReadMode()}");
        int fails = 0;
        try
        {
            foreach (int pct in new[] { 60, 30 })
            {
                var flat = saved.Select(x => (x.t, pct)).ToList();
                Hardware.Fans.Apply(Hardware.FanMode.Custom, 50, flat);
                var table = Hardware.Fans.ReadCurve();
                bool tableOk = table.All(x => Math.Abs(x.percent - pct) <= 1);
                Log.Write($"CURVETEST {pct}%: mode={Hardware.Fans.ReadMode()} table ok={tableOk} [{string.Join(" ", table.Select(x => $"{x.temp}:{x.percent}"))}]");
                int lastDuty = -1;
                for (int i = 0; i < 15; i++)
                {
                    Thread.Sleep(1000);
                    var s = Hardware.Sensors.Read();
                    lastDuty = s.Fan1Pct;
                    Log.Write($"CURVETEST {pct}% t+{i + 1}s: cpu {s.CpuTemp} gpu {s.GpuTemp} duty {s.Fan1Pct}/{s.Fan2Pct}% rpm {s.Fan1Rpm}/{s.Fan2Rpm}");
                }
                bool follows = Math.Abs(lastDuty - pct) <= 5;
                Log.Write($"CURVETEST {pct}% result: table={tableOk} duty follows curve={follows}");
                if (!tableOk || !follows) fails++;
            }
        }
        finally
        {
            Hardware.Fans.WriteCurve(saved);
            Hardware.Fans.Apply(st.UsesCurve(mode.Index) ? Hardware.FanMode.Custom : mode.Fan, st.FixedFanPercent, saved);
            Log.Write($"CURVETEST restored: fans={Hardware.Fans.ReadMode()} ({(fails == 0 ? "PASS" : "FAIL")})");
        }
        return fails;
    }

    /// <summary>
    /// Fan-off zone on real hardware: 0% below a threshold a few degrees above the current CPU temperature, 35%+ above it.
    /// A: idle 25 s, fans should stop. B: load every core until the CPU passes the threshold, fans must start again.
    /// C: stop the load and watch 60 s of cool-down (does it cycle on/off at the boundary?). Aborts if the CPU passes
    /// 85 °C with the fans still off. The mode's own fans and the saved curve are always put back.
    /// </summary>
    private static int FanOffTest()
    {
        var st = Core.Settings.Load();
        var mode = Modes.ById(Hardware.Sensors.OnAc ? st.ModeAc : st.ModeDc);
        var saved = st.CurveTemps.Zip(st.CurvePercents, (t, p) => (t, p)).ToList();
        var s0 = Hardware.Sensors.Read();
        int th = Math.Max(s0.CpuTemp, s0.GpuTemp) + 6;
        var curve = new List<(int, int)> { (0, 0) };
        for (int i = 0; i < 14; i++) curve.Add((th + i * 3, Math.Min(100, 35 + i * 5)));
        Log.Write($"FANOFF start: mode {mode.Id}, cpu {s0.CpuTemp} gpu {s0.GpuTemp}, threshold {th} °C");
        bool stopped = false, restarted = false, aborted = false; int transitions = 0;
        var stop = new ManualResetEventSlim();
        var load = new List<Thread>();
        try
        {
            Hardware.Fans.Apply(Hardware.FanMode.Custom, 50, curve);
            Log.Write($"FANOFF table [{string.Join(" ", Hardware.Fans.ReadCurve().Select(x => $"{x.temp}:{x.percent}"))}] mode={Hardware.Fans.ReadMode()}");
            int lastOn = -1;
            bool Sample(string phase, int sec)
            {
                var s = Hardware.Sensors.Read();
                Log.Write($"FANOFF {phase} t+{sec}s: cpu {s.CpuTemp} gpu {s.GpuTemp} duty {s.Fan1Pct}/{s.Fan2Pct}% rpm {s.Fan1Rpm}/{s.Fan2Rpm}");
                int on = s.Fan1Rpm > 300 || s.Fan2Rpm > 300 ? 1 : 0;
                if (lastOn >= 0 && on != lastOn) transitions++;
                lastOn = on;
                if (phase == "A" && s.Fan1Rpm < 300 && s.Fan2Rpm < 300) stopped = true;
                if (phase != "A" && stopped && on == 1) restarted = true;
                if (s.CpuTemp >= 85 && on == 0) { aborted = true; Log.Write("FANOFF ABORT: hot with fans off"); return false; }
                return true;
            }
            for (int i = 1; i <= 25; i++) { Thread.Sleep(1000); if (!Sample("A", i)) return 9; }
            for (int c = 0; c < Environment.ProcessorCount; c++)
            {
                var t = new Thread(() => { double x = 1; while (!stop.IsSet) x = Math.Sqrt(x + 1.0001); }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
                t.Start(); load.Add(t);
            }
            for (int i = 1; i <= 90; i++)
            {
                Thread.Sleep(1000);
                if (!Sample("B", i)) return 9;
                var s = Hardware.Sensors.Read();
                if (restarted && i >= 10 && Math.Max(s.CpuTemp, s.GpuTemp) >= th + 4) break;
            }
            stop.Set();
            transitions = 0;
            for (int i = 1; i <= 60; i++) { Thread.Sleep(1000); if (!Sample("C", i)) return 9; }
        }
        finally
        {
            stop.Set();
            Hardware.Fans.WriteCurve(saved);
            Hardware.Fans.Apply(st.UsesCurve(mode.Index) ? Hardware.FanMode.Custom : mode.Fan, st.FixedFanPercent, saved);
            Log.Write($"FANOFF restored: fans={Hardware.Fans.ReadMode()} stopped={stopped} restarted={restarted} cooldown on/off changes={transitions} aborted={aborted}");
        }
        return stopped && restarted ? 0 : 1;
    }

    private static int KbTest()
    {
        using var kb = Hardware.KeyboardLight.Open();
        if (kb == null) { Log.Write("kbtest: keyboard not found"); return 2; }
        Log.Write($"kbtest start, GCC processes={System.Diagnostics.Process.GetProcessesByName("GCC").Length}, initial={kb.GetBrightness()}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // the 2026-11-25 incident pattern: write then immediately query. The query must now be refused locally.
        foreach (int lv in new[] { 100, 32, 100, 64, 0, 100 })
        {
            var t0 = sw.ElapsedMilliseconds;
            bool ok = kb.SetBrightness(lv);
            var r1 = kb.GetBrightness();
            Thread.Sleep(1600);
            var r2 = kb.GetBrightness();
            Log.Write($"kbtest set {lv,3}% ok={ok} write={sw.ElapsedMilliseconds - t0 - 1600}ms immediate-read={r1?.ToString() ?? "refused"} read@1.6s={r2?.ToString() ?? "null"} tripped={kb.Tripped}");
            if (kb.Tripped) return 3;
        }
        return 0;
    }

    private static int KbSweep(int totalMs)
    {
        using var kb = Hardware.KeyboardLight.Open();
        if (kb == null) return 2;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int half = totalMs / 2, last = -1;
        while (sw.ElapsedMilliseconds < totalMs)
        {
            long t = sw.ElapsedMilliseconds;
            double x = t < half ? (double)t / half : 1.0 - (double)(t - half) / half;
            int level = (int)Math.Round(x * 50) * 2;
            if (level != last) { if (!kb.SetBrightness(level)) break; last = level; }
            Thread.Sleep(20);
        }
        Log.Write($"kbsweep writes done in {sw.ElapsedMilliseconds} ms, tripped={kb.Tripped} {kb.TripReason}");
        kb.SetBrightness(100);
        Thread.Sleep(1600);
        Log.Write($"kbsweep read@1.6s={kb.GetBrightness()?.ToString() ?? "null"} tripped={kb.Tripped} {kb.TripReason}");
        return kb.Tripped ? 3 : 0;
    }
}
