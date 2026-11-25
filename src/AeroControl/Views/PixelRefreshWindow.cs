using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace AeroControl.Views;

/// <summary>
/// OLED pixel refresh (GCC plays a video for this): full-screen solid fields cycling through
/// white, red, green, blue and black for 5 minutes, then closes. Any key, click or Esc stops it.
/// </summary>
public sealed class PixelRefreshWindow : Window
{
    private static readonly Color[] Seq = { Colors.White, Colors.Red, Color.FromRgb(0, 255, 0), Colors.Blue, Colors.Black };
    private readonly DispatcherTimer _t = new() { Interval = TimeSpan.FromSeconds(2) };
    private int _i;
    private readonly DateTime _end = DateTime.Now.AddMinutes(5);

    public PixelRefreshWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        Topmost = true;
        Cursor = Cursors.None;
        Background = new SolidColorBrush(Seq[0]);
        _t.Tick += (_, _) =>
        {
            if (DateTime.Now >= _end) { Close(); return; }
            Background = new SolidColorBrush(Seq[++_i % Seq.Length]);
        };
        Loaded += (_, _) => _t.Start();
        Closed += (_, _) => _t.Stop();
        KeyDown += (_, _) => Close();
        MouseDown += (_, _) => Close();
    }
}
