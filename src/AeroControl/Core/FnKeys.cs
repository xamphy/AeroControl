using System;
using System.Linq;
using System.Windows.Threading;
using AeroControl.Display;
using AeroControl.Input;
using AeroControl.Views;

namespace AeroControl.Core;

/// <summary>
/// What each Fn key does, plus its on-screen popup. Mirrors GCC's OSDwindow behaviour:
/// brightness ±10 (to 0 / 100 at the ends), fan key cycles modes, mic key toggles the default mic.
/// Keys the hardware handles itself (keyboard light, touchpad, Fn/Win lock) only get the popup.
/// </summary>
public sealed class FnKeys : IDisposable
{
    private const string IcoSun = "M12 8a4 4 0 1 0 0 8 4 4 0 1 0 0-8zM12 2v2M12 20v2M2 12h2M20 12h2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M4.9 19.1l1.4-1.4M17.7 6.3l1.4-1.4";
    private const string IcoKeyboard = "M3 6h18v12H3zM7 10h.01M11 10h.01M15 10h.01M7 14h10";
    private const string IcoMic = "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3";
    private const string IcoMicOff = "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5 11a7 7 0 0 0 14 0M12 18v3M3 3l18 18";
    private const string IcoTouchpad = "M4 5h16v14H4zM4 15h16M12 15v4";
    private const string IcoLock = "M6 11h12v9H6zM8 11V8a4 4 0 0 1 8 0v3";
    private const string IcoWin = "M4 4h7v7H4zM13 4h7v7h-7zM4 13h7v7H4zM13 13h7v7h-7z";

    private readonly Controller _c;
    private readonly Dispatcher _ui;
    private readonly HotkeyListener _listener;
    private readonly OsdWindow _osd = new();
    private readonly Action _refreshUi;

    public FnKeys(Controller c, Dispatcher ui, Action refreshUi)
    {
        _c = c; _ui = ui; _refreshUi = refreshUi;
        _listener = new HotkeyListener();
        _listener.Hotkey += (_, e) => _ui.BeginInvoke(new Action(() => Handle(e)));
    }

    private void Handle(HotkeyEventArgs e)
    {
        switch (e.Kind)
        {
            case HotkeyKind.BrightnessDown:
            case HotkeyKind.BrightnessUp:
            {
                int b = Panel.GetBrightness();
                if (b < 0) return;
                b = e.Kind == HotkeyKind.BrightnessUp ? Math.Min(100, b + 10) : (b < 10 ? 0 : b - 10);
                Panel.SetBrightness(b);
                _osd.ShowLevel(IcoSun, "Brightness", b);
                _refreshUi();
                break;
            }
            case HotkeyKind.FanKey:
            {
                // Fn+Esc cycles Silent → Balanced → Performance; Turbo is only chosen in the app
                var cycle = Modes.All.Where(m => m.Id != "turbo").ToList();
                int at = cycle.FindIndex(m => m.Id == _c.CurrentMode.Id);
                var next = cycle[(at + 1) % cycle.Count];
                _c.SelectMode(next.Id);
                string glyph = next.Index switch
                {
                    0 => "M11 5L6 9H3v6h3l5 4zM22 9l-6 6M16 9l6 6",
                    1 => "M12 3v18M5 8h14M7 8l-3 7h6zM17 8l-3 7h6z",
                    2 => "M13 2L4 14h7l-1 8 9-12h-7z",
                    _ => "M12 2c3 4 5 6 5 10a5 5 0 0 1-10 0c0-2 1-3 2-4 0 2 1 3 2 3 0-3 1-6 1-9z",
                };
                _osd.ShowState(glyph, next.Name, $"{Modes.FanLabel(next.Fan)} fans");
                break;
            }
            case HotkeyKind.MicMute:
            {
                bool? muted = Microphone.Toggle();
                if (muted == null) return;
                _osd.ShowState(muted.Value ? IcoMicOff : IcoMic, "Microphone", muted.Value ? "Muted" : "On");
                break;
            }
            case HotkeyKind.KeyboardLight:
                if (e.Value >= 0)
                {
                    // the keyboard already changed its own level; the report carries it
                    _osd.ShowLevel(IcoKeyboard, "Keyboard", e.Value);
                    _c.KeyboardReported(e.Value, () => _ui.BeginInvoke(_refreshUi));
                }
                else
                    _c.ReadKeyboard(lvl => _ui.BeginInvoke(new Action(() =>
                    {
                        if (lvl < 0) return;
                        _osd.ShowLevel(IcoKeyboard, "Keyboard", lvl);
                        _refreshUi();
                    })));
                break;
            case HotkeyKind.TouchpadToggled:
                if (e.Value >= 0) { _osd.ShowState(IcoTouchpad, "Touchpad", e.Value == 1 ? "On" : "Off"); break; }
                // the keyboard toggles the touchpad itself; Windows records the new state shortly after
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                t.Tick += (_, _) => { t.Stop(); _osd.ShowState(IcoTouchpad, "Touchpad", TouchpadEnabled() ? "On" : "Off"); };
                t.Start();
                break;
            case HotkeyKind.FnLock:
                _osd.ShowState(IcoLock, "Fn Lock", e.Value == 1 ? "On" : "Off");
                break;
            case HotkeyKind.WinLock:
                _osd.ShowState(IcoWin, "Windows key", e.Value == 1 ? "Locked" : "Unlocked");
                break;
        }
    }

    private static bool TouchpadEnabled()
    {
        using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad\Status");
        return k == null || Convert.ToInt32(k.GetValue("Enabled", 1)) != 0;
    }

    public void Dispose() { _listener.Dispose(); _osd.Close(); }
}
