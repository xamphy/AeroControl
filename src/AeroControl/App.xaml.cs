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
        if (args.Contains("--kbtest")) { Shutdown(KbTest()); return; }
        if (args.Contains("--kbsweep")) { Shutdown(KbSweep(8000)); return; }

        // ---- single instance: a second launch just shows the running one ----
        _single = new Mutex(true, "AeroControl.Single", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "AeroControl.Show");
        if (!first) { _showSignal.Set(); Shutdown(0); return; }

        bool firstRun = !File.Exists(Path.Combine(Log.Dir, "settings.json"));
        _controller = new Controller();
        if (firstRun) ImportFromGcc(_controller.Settings);
        Themes.ThemeManager.Apply(_controller.Settings.DarkAppearance, _controller.Settings.Accent);

        _vm = new MainViewModel(_controller);
        _main = new MainWindow(_vm);
        _popover = new PopoverWindow(_vm, page => _main.ShowAndActivate(page));
        _tray = new TrayIcon(() => _popover.Toggle(), () => _main.ShowAndActivate(), Quit);
        try { _fnKeys = new FnKeys(_controller, Dispatcher, () => _vm.RefreshFromHardware()); }
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
        _popover.Toggle();
        await System.Threading.Tasks.Task.Delay(600);
        Save(_popover, Path.Combine(dir, "popover.png"));
        Quit();

        static void Save(Window w, string file)
        {
            w.UpdateLayout();
            var root = (FrameworkElement)w.Content;
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                (int)(root.ActualWidth * dpi.DpiScaleX), (int)(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY,
                System.Windows.Media.PixelFormats.Pbgra32);
            var bg = new System.Windows.Media.DrawingVisual();
            using (var dc = bg.RenderOpen())
                dc.DrawRectangle(w.Background is System.Windows.Media.SolidColorBrush b && b.Color.A > 0 ? b : System.Windows.Media.Brushes.Black, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            rtb.Render(bg);
            rtb.Render(root);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using var fs = File.Create(file);
            enc.Save(fs);
        }
    }

    // ---------------- diagnostics kept from bring-up ----------------

    private static int KbTest()
    {
        using var kb = Hardware.KeyboardLight.Open();
        if (kb == null) { Log.Write("kbtest: keyboard not found"); return 2; }
        Log.Write($"kbtest start, GCC processes={System.Diagnostics.Process.GetProcessesByName("GCC").Length}, initial={kb.GetBrightness()}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (int lv in new[] { 100, 0, 50, 0, 20, 100 })
        {
            bool ok = kb.SetBrightness(lv);
            Thread.Sleep(100);
            var r1 = kb.GetBrightness();
            Thread.Sleep(1900);
            Log.Write($"kbtest t={sw.ElapsedMilliseconds - 2000,5}ms set {lv,3}% ok={ok} read@0.1s={r1} read@2s={kb.GetBrightness()}");
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
            if (level != last) { kb.SetBrightness(level); last = level; }
            Thread.Sleep(20);
        }
        kb.SetBrightness(0);
        return 0;
    }
}
