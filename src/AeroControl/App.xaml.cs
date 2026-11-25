using System;
using System.Linq;
using System.Windows;

namespace AeroControl;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var args = e.Args;
        int probe = Array.IndexOf(args, "--probe");
        if (probe >= 0)
        {
            string path = probe + 1 < args.Length ? args[probe + 1] : "probe.txt";
            Shutdown(Probe.Run(path));
            return;
        }

        if (Array.IndexOf(args, "--kbtest") >= 0)
        {
            Shutdown(KbTest());
            return;
        }
        if (Array.IndexOf(args, "--kbsweep") >= 0)
        {
            Shutdown(KbSweep(8000));
            return;
        }

        ShutdownMode = ShutdownMode.OnMainWindowClose;
        var w = new MainWindow();
        MainWindow = w;
        w.Show();
    }

    /// <summary>Set a few levels slowly; log the readback right after and again after 2 s (catches GCC overriding).</summary>
    private static int KbTest()
    {
        using var kb = Hardware.KeyboardLight.Open();
        if (kb == null) { Log.Write("kbtest: keyboard not found"); return 2; }
        var gcc = System.Diagnostics.Process.GetProcessesByName("GCC").Length;
        Log.Write($"kbtest start, GCC processes={gcc}, initial={kb.GetBrightness()}");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (int lv in new[] { 100, 0, 50, 0, 20, 100 })
        {
            bool ok = kb.SetBrightness(lv);
            System.Threading.Thread.Sleep(100);
            var r1 = kb.GetBrightness();
            System.Threading.Thread.Sleep(1900);
            var r2 = kb.GetBrightness();
            Log.Write($"kbtest t={sw.ElapsedMilliseconds - 2000,5}ms set {lv,3}% ok={ok} read@0.1s={r1} read@2s={r2}");
        }
        return 0;
    }

    /// <summary>Ramp the keyboard backlight 0 → 100 → 0 over totalMs, on a fixed time base.</summary>
    private static int KbSweep(int totalMs)
    {
        using var kb = Hardware.KeyboardLight.Open();
        if (kb == null) { Log.Write("kbsweep: keyboard not found"); return 2; }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int half = totalMs / 2, last = -1;
        while (sw.ElapsedMilliseconds < totalMs)
        {
            long t = sw.ElapsedMilliseconds;
            double x = t < half ? (double)t / half : 1.0 - (double)(t - half) / half;
            int level = (int)Math.Round(x * 50) * 2; // device resolution is 2%
            if (level != last) { kb.SetBrightness(level); last = level; }
            System.Threading.Thread.Sleep(20);
        }
        kb.SetBrightness(0);
        Log.Write($"kbsweep done in {sw.ElapsedMilliseconds} ms");
        return 0;
    }
}
