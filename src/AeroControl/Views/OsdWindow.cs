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
/// macOS-style popup for Fn keys (replaces GCC's OSDwindow and the Windows volume flyout): a translucent
/// notification card in the top-right corner, like macOS's Control Center HUDs. Icon tile on the left; title
/// with the value on the right, and below it a level bar or a status line. Slides in from the right, never
/// takes focus, is click-through, and re-uses itself while visible.
/// </summary>
public sealed class OsdWindow : Window
{
    private const double CardW = 320, Shadow = 18, Gap = 10;
    private readonly AeroControl.Views.Icon _icon = new() { Width = 22, Height = 22, Thickness = 1.8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _value = new() { FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 12, Margin = new Thickness(0, 3, 0, 0) };
    private readonly Grid _bar = new() { Height = 6, Margin = new Thickness(0, 8, 0, 0) };
    private readonly Border _fill = new() { CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromMilliseconds(1600) };
    private double _level;

    public OsdWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.Height;
        Width = CardW + 2 * Shadow;
        Focusable = false;

        var track = new Border { CornerRadius = new CornerRadius(3) };
        track.SetResourceReference(Border.BackgroundProperty, "Field");
        _fill.SetResourceReference(Border.BackgroundProperty, "Text");
        _bar.Children.Add(track);
        _bar.Children.Add(_fill);
        _bar.SizeChanged += (_, _) => { _fill.BeginAnimation(WidthProperty, null); _fill.Width = _bar.ActualWidth * _level; };

        _title.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        _value.SetResourceReference(TextBlock.ForegroundProperty, "Sub");
        _status.SetResourceReference(TextBlock.ForegroundProperty, "Sub");
        _icon.SetResourceReference(AeroControl.Views.Icon.StrokeProperty, "Text");
        foreach (var t in new[] { _title, _value, _status }) t.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");

        var tile = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Child = _icon, VerticalAlignment = VerticalAlignment.Center };
        tile.SetResourceReference(Border.BackgroundProperty, "Field");

        var head = new Grid();
        head.Children.Add(_title);
        head.Children.Add(_value);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        text.Children.Add(head);
        text.Children.Add(_bar);
        text.Children.Add(_status);

        var row = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        row.Children.Add(tile);
        row.Children.Add(text);

        var card = new Border
        {
            Width = CardW, CornerRadius = new CornerRadius(18), BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 11, 16, 11), Margin = new Thickness(Shadow),
            Child = row, RenderTransform = _slide,
            Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.35 },
        };
        card.SetResourceReference(Border.BackgroundProperty, "Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Line");
        Content = card;

        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
            fade.Completed += (_, _) => { if (!_hide.IsEnabled) Hide(); };
            BeginAnimation(OpacityProperty, fade);
            _slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(16, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        };
        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = GetWindowLong(h, -20);
            SetWindowLong(h, -20, ex | 0x20 /*TRANSPARENT*/ | 0x80 /*TOOLWINDOW*/ | 0x08000000 /*NOACTIVATE*/);
        };
    }

    /// <summary>Show a level (0..100) with a bar.</summary>
    public void ShowLevel(string iconPath, string title, int percent)
    {
        _level = Math.Max(0, Math.Min(100, percent)) / 100.0;
        _bar.Visibility = Visibility.Visible;
        _status.Visibility = Visibility.Collapsed;
        _value.Text = $"{percent}%";
        // glide the fill when the card is already up (repeated presses); otherwise set it directly
        if (IsVisible && _bar.ActualWidth > 0)
            _fill.BeginAnimation(WidthProperty, new DoubleAnimation(_bar.ActualWidth * _level, TimeSpan.FromMilliseconds(90)));
        else { _fill.BeginAnimation(WidthProperty, null); _fill.Width = _bar.ActualWidth * _level; }
        Present(iconPath, title);
    }

    /// <summary>Show an on/off or named state.</summary>
    public void ShowState(string iconPath, string title, string status)
    {
        _bar.Visibility = Visibility.Collapsed;
        _status.Visibility = Visibility.Visible;
        _status.Text = status;
        _value.Text = "";
        Present(iconPath, title);
    }

    private void Present(string iconPath, string title)
    {
        _icon.Data = iconPath;
        _title.Text = title;
        // top-right corner of the work area; the window is larger than the card by the shadow margin
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width + Shadow - Gap;
        Top = wa.Top + Gap - Shadow;
        BeginAnimation(OpacityProperty, null);
        if (!IsVisible)
        {
            Opacity = 0; Show();
            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
            _slide.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }
        else { Opacity = 1; _slide.BeginAnimation(TranslateTransform.XProperty, null); _slide.X = 0; }
        _hide.Stop(); _hide.Start();
    }

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
}
