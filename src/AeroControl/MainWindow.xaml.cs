using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using AeroControl.ViewModels;
using AeroControl.Views;

namespace AeroControl;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CurveEditor.Committed += () => Vm.CommitCurve();
        SourceInitialized += (_, _) => Win11Frame(new WindowInteropHelper(this).Handle);
    }

    /// <summary>Rounded Windows 11 corners + dark caption (the native frame is hidden by WindowChrome).</summary>
    private void Win11Frame(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int dark = Vm.DarkAppearance ? 1 : 0;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int value, int size);

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Hide(); // keeps running in the tray

    private void GoBattery_Click(object sender, RoutedEventArgs e) => Vm.Page = "battery";
    private void GoDisplay_Click(object sender, RoutedEventArgs e) => Vm.Page = "display";
    private void Accent_Click(object sender, RoutedEventArgs e) => Vm.SetAccent((string)((Button)sender).Tag);
    private void PixelRefresh_Click(object sender, RoutedEventArgs e) => new PixelRefreshWindow().Show();

    public void ShowAndActivate(string page = null)
    {
        if (page != null) Vm.Page = page;
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }
}
