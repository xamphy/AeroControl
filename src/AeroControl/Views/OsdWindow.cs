using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace AeroControl.Views;

/// <summary>
/// macOS-style HUD for Fn keys (replaces GCC's OSDwindow popups): a translucent rounded square in the
/// lower middle of the screen with an icon, a title and either a 16-segment level bar or a status line.
/// Never takes focus, is click-through, and re-uses itself while visible.
/// </summary>
public sealed class OsdWindow : Window
{
    private const int Segments = 16;
    private readonly AeroControl.Views.Icon _icon = new() { Width = 64, Height = 64, Thickness = 1.6 };
    private readonly TextBlock _title = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
    private readonly TextBlock _status = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
    private readonly StackPanel _bar = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
    private readonly Border[] _cells = new Border[Segments];
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1500) };

    public OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        Width = 236; Height = 236;
        Focusable = false;

        for (int i = 0; i < Segments; i++)
        {
            _cells[i] = new Border { Width = 8, Height = 6, Margin = new Thickness(1), CornerRadius = new CornerRadius(1.5) };
            _bar.Children.Add(_cells[i]);
        }
        _title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Sub");
        _icon.SetResourceReference(AeroControl.Views.Icon.StrokeProperty, "Text");
        _title.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        _status.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(_icon);
        stack.Children.Add(_title);
        stack.Children.Add(_bar);
        stack.Children.Add(_status);

        var card = new Border
        {
            Width = 200, Height = 200, CornerRadius = new CornerRadius(22), BorderThickness = new Thickness(1),
            Child = stack,
            Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 6, Opacity = 0.4 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        Content = card;

        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            fade.Completed += (_, _) => { if (!_hide.IsEnabled) Hide(); };
            BeginAnimation(OpacityProperty, fade);
        };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(h, -20);
            SetWindowLong(h, -20, ex | 0x20 /*TRANSPARENT*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/);
        };
    }

    /// <summary>Show a level (0..100) with a segmented bar.</summary>
    public void ShowLevel(string iconPath, string title, int percent)
    {
        int lit = (int)Math.Round(Math.Max(0, Math.Min(100, percent)) / 100.0 * Segments);
        for (int i = 0; i < Segments; i++)
            _cells[i].SetResourceReference(Border.BackgroundProperty, i < lit ? "Text" : "Field");
        _bar.Visibility = Visibility.Visible;
        _status.Text = $"{percent}%";
        Present(iconPath, title);
    }

    /// <summary>Show an on/off or named state.</summary>
    public void ShowState(string iconPath, string title, string status)
    {
        _bar.Visibility = Visibility.Collapsed;
        _status.Text = status;
        Present(iconPath, title);
    }

    private void Present(string iconPath, string title)
    {
        _icon.Data = iconPath;
        _title.Text = title;
        var wa = SystemParameters.WorkArea;
        Left = wa.Left + (wa.Width - Width) / 2;
        Top = wa.Bottom - Height - 90;
        BeginAnimation(OpacityProperty, null);
        if (!IsVisible) { Opacity = 0; Show(); BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120))); }
        else Opacity = 1;
        _hide.Stop(); _hide.Start();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
}
