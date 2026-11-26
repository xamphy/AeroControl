using System;
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

        int sweep = Array.IndexOf(args, "--sweep");
        if (sweep >= 0) { Sweep(sweep + 1 < args.Length && !args[sweep + 1].StartsWith("--") ? args[sweep + 1] : Path.Combine(Log.Dir, "sweeps", DateTime.Now.ToString("yyyyMMdd-HHmm"))); return; }

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

    /// <summary>
    /// Settings sweep for logging: really switches each power mode, keyboard level and charge limit, waits for the
    /// hardware to settle, screenshots the affected pages and writes what the hardware reports to sweep.log in the
    /// same folder. Everything is put back as it was at the end. Run with the normal app closed (one HID owner).
    /// </summary>
    private async void Sweep(string dir)
    {
        Directory.CreateDirectory(dir);
        var logFile = Path.Combine(dir, "sweep.log");
        File.WriteAllText(logFile, "");
        int n = 0;
        async System.Threading.Tasks.Task Step(string name, params string[] pages)
        {
            n++;
            string hw = await _controller.DescribeAsync();
            string line = $"{DateTime.Now:HH:mm:ss} #{n:00} {name}: {hw}";
            File.AppendAllText(logFile, line + Environment.NewLine);
            Log.Write("SWEEP " + line);
            foreach (var page in pages)
            {
                string file = $"{n:00}-{name}-{page}.png";
                if (page == "popover")
                {
                    _popover.Toggle(); await System.Threading.Tasks.Task.Delay(700);
                    SaveWindow(_popover, Path.Combine(dir, file));
                    _popover.Toggle(); await System.Threading.Tasks.Task.Delay(300);
                }
                else
                {
                    _vm.Page = page; await System.Threading.Tasks.Task.Delay(500);
                    SaveFull(_main, Path.Combine(dir, file));
                }
            }
        }

        var st = _controller.Settings;
        _main.Show();
        await System.Threading.Tasks.Task.Delay(3000);
        string origMode = _controller.CurrentMode.Id;
        var origCharge = st.Charge;
        await _controller.DescribeAsync();
        int origKb = _controller.KeyboardLevel;
        File.AppendAllText(logFile, $"start: mode {origMode}, kb {origKb}%, charge {origCharge}, kbFollow {st.KbFollowMode}{Environment.NewLine}");
        try
        {
            await Step("start", "home");

            // power modes: switch for real, let the fans settle, then log + shoot
            foreach (var m in Modes.All)
            {
                _controller.SelectMode(m.Id);
                await System.Threading.Tasks.Task.Delay(9000);
                await Step($"mode-{m.Id}", "home", "perf", "popover");
            }
            _controller.SelectMode(origMode);
            await System.Threading.Tasks.Task.Delay(6000);

            // keyboard backlight levels (the keyboard's own Fn+Space steps are 0/48/64/100)
            var osd = new Views.OsdWindow();
            foreach (int lvl in new[] { 0, 24, 48, 64, 100 })
            {
                _controller.SetKeyboard(lvl, remember: false);
                _vm.RefreshFromHardware();
                await System.Threading.Tasks.Task.Delay(1500);
                osd.ShowLevel("M3 6h18v12H3zM7 10h.01M11 10h.01M15 10h.01M7 14h10", "Keyboard", lvl);
                await System.Threading.Tasks.Task.Delay(500);
                SaveWindow(osd, Path.Combine(dir, $"{n + 1:00}-kb-{lvl}-osd.png"));
                await Step($"kb-{lvl}", "keyboard", "popover");
            }

            // charge limit
            foreach (var l in new[] { Hardware.ChargeLimit.Full, Hardware.ChargeLimit.Balanced, Hardware.ChargeLimit.MaxHealth })
            {
                _controller.SetCharge(l);
                await System.Threading.Tasks.Task.Delay(1500);
                await Step($"charge-{Hardware.Battery.StopPercent(l)}", "battery", "home");
            }
        }
        catch (Exception ex) { File.AppendAllText(logFile, "SWEEP ERROR " + ex + Environment.NewLine); }
        finally
        {
            _controller.SelectMode(origMode);
            if (origKb >= 0) _controller.SetKeyboard(origKb, remember: false);
            _controller.SetCharge(origCharge);
            await System.Threading.Tasks.Task.Delay(4000);
            await Step("restored", "home");
            Quit();
        }

        static void SaveWindow(Window w, string file) => SaveVisual((FrameworkElement)w.Content, w, file, w.Background);

        // the main window with its scroll content at full height: window chrome + sidebar + whole page
        static void SaveFull(Window w, string file)
        {
            var sv = FindSv(w);
            var root = (FrameworkElement)w.Content;
            double extra = sv == null ? 0 : Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
            double old = w.Height;
            if (extra > 0) { w.Height = old + extra; w.UpdateLayout(); }
            SaveVisual(root, w, file, w.Background);
            if (extra > 0) { w.Height = old; w.UpdateLayout(); }
        }

        static System.Windows.Controls.ScrollViewer FindSv(DependencyObject o)
        {
            if (o is System.Windows.Controls.ScrollViewer sv) return sv;
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(o); i++)
                if (FindSv(System.Windows.Media.VisualTreeHelper.GetChild(o, i)) is { } f) return f;
            return null;
        }

        static void SaveVisual(FrameworkElement root, Window w, string file, System.Windows.Media.Brush background)
        {
            root.UpdateLayout();
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            double fw = root.ActualWidth + root.Margin.Left + root.Margin.Right, fh = root.ActualHeight + root.Margin.Top + root.Margin.Bottom;
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(fw * dpi.DpiScaleX), (int)(fh * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            if (background is System.Windows.Media.SolidColorBrush b && b.Color.A > 0)
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

    /// <summary>Render every page of the main window and the popover to PNGs (read-only; for design review).</summary>
    private async void Snapshots(string dir)
    {
        Directory.CreateDirectory(dir);
        _main.Show();
        await System.Threading.Tasks.Task.Delay(2500); // first sensor poll
        for (int i = 0; i < 40 && !Hardware.Usage.Ready; i++) await System.Threading.Tasks.Task.Delay(250); // load counters warm up
        await System.Threading.Tasks.Task.Delay(2500); // a poll with load values
        var st = _controller.Settings;
        string[] pages = { "home", "perf", "battery", "display", "keyboard", "about", "settings" };
        foreach (bool dark in new[] { true, false })
        {
            // the light pass is in memory only: nothing is saved
            Themes.ThemeManager.Apply(dark, st.Accent);
            string sfx = dark ? "" : "-light";
            await System.Threading.Tasks.Task.Delay(300);
            foreach (var page in pages)
            {
                _vm.Page = page;
                await System.Threading.Tasks.Task.Delay(400);
                Save(_main, Path.Combine(dir, $"main-{page}{sfx}.png"));
                SaveFullPage(Path.Combine(dir, $"page-{page}{sfx}.png"));
            }
            // fan curve editor, shown in memory only (nothing saved or sent to the fans)
            var saved = st.CustomCurve;
            st.CustomCurve = new[] { true, true, true, true };
            _vm.Page = "home"; _vm.Page = "perf";
            await System.Threading.Tasks.Task.Delay(2200); // a sensor poll, so the live dot has a temperature
            SaveFullPage(Path.Combine(dir, $"page-perf-curve{sfx}.png"));
            st.CustomCurve = saved;
            _vm.Page = "home";
            _popover.Toggle();
            await System.Threading.Tasks.Task.Delay(700);
            Save(_popover, Path.Combine(dir, $"popover{sfx}.png"));
            _popover.Toggle();
            await System.Threading.Tasks.Task.Delay(300);
        }
        Themes.ThemeManager.Apply(st.DarkAppearance, st.Accent);
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

        // The whole page, including what is scrolled out of view: the scroll viewer's content at its full height.
        void SaveFullPage(string file)
        {
            var sv = FindScroll(_main);
            if (sv?.Content is not FrameworkElement content) return;
            content.UpdateLayout();
            double fw = content.ActualWidth + content.Margin.Left + content.Margin.Right;
            double fh = content.ActualHeight + content.Margin.Top + content.Margin.Bottom;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(_main);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap((int)(fw * dpi.DpiScaleX), (int)(fh * dpi.DpiScaleY),
                dpi.PixelsPerInchX, dpi.PixelsPerInchY, System.Windows.Media.PixelFormats.Pbgra32);
            var bg = new System.Windows.Media.DrawingVisual();
            using (var dc = bg.RenderOpen()) dc.DrawRectangle(_main.Background, null, new Rect(0, 0, fw, fh));
            rtb.Render(bg);
            // render through a VisualBrush so the scroll offset does not shift the picture
            var v = new System.Windows.Media.DrawingVisual();
            using (var dc = v.RenderOpen())
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(content) { Stretch = System.Windows.Media.Stretch.None, AlignmentX = System.Windows.Media.AlignmentX.Left, AlignmentY = System.Windows.Media.AlignmentY.Top },
                    null, new Rect(content.Margin.Left, content.Margin.Top, content.ActualWidth, content.ActualHeight));
            rtb.Render(v);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(file);
            enc.Save(fs);
        }

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
