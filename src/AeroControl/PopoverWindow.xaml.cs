using System;
using System.Windows;
using System.Windows.Media.Animation;
using AeroControl.ViewModels;

namespace AeroControl;

/// <summary>Control Center–style popover above the tray icon. Hides when it loses focus.</summary>
public partial class PopoverWindow : Window
{
    private readonly Action<string> _openMain;
    private DateTime _hiddenAt;

    public PopoverWindow(MainViewModel vm, Action<string> openMain)
    {
        InitializeComponent();
        DataContext = vm;
        _openMain = openMain;
        Deactivated += (_, _) => { Hide(); _hiddenAt = DateTime.Now; };
    }

    /// <summary>Toggle from a tray click. A click that just closed it (by deactivation) must not reopen it.</summary>
    public void Toggle()
    {
        if (IsVisible) { Hide(); return; }
        if ((DateTime.Now - _hiddenAt).TotalMilliseconds < 300) return;
        Show();
        UpdateLayout();
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - ActualWidth + 4;
        Top = wa.Bottom - ActualHeight + 4;
        Activate();
        var a = new DoubleAnimation(24, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        Slide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, a);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e) { Hide(); _openMain(null); }
    private void Fans_Click(object sender, RoutedEventArgs e) { Hide(); _openMain("perf"); }
    private void Charge_Click(object sender, RoutedEventArgs e) => ((MainViewModel)DataContext).CycleCharge();
}
