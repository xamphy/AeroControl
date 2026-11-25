using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace AeroControl.Views;

/// <summary>
/// The AERO 14's keyboard drawn to scale (15 units wide), lit like the white backlight:
/// legends and key edges glow with Level (0..1), and go dim grey when the light is off.
/// </summary>
public sealed class KeyboardPreview : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(KeyboardPreview), new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KeyFillProperty = DependencyProperty.Register(
        nameof(KeyFill), typeof(Brush), typeof(KeyboardPreview), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DeckProperty = DependencyProperty.Register(
        nameof(Deck), typeof(Brush), typeof(KeyboardPreview), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty DimProperty = DependencyProperty.Register(
        nameof(Dim), typeof(Brush), typeof(KeyboardPreview), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public Brush KeyFill { get => (Brush)GetValue(KeyFillProperty); set => SetValue(KeyFillProperty, value); }
    public Brush Deck { get => (Brush)GetValue(DeckProperty); set => SetValue(DeckProperty, value); }
    public Brush Dim { get => (Brush)GetValue(DimProperty); set => SetValue(DimProperty, value); }

    // Each row: (width in units, legend). Function row keys are half height.
    private static readonly (double w, string l)[][] Rows =
    {
        new[] { (1.0, "esc"), (1.0, "F1"), (1.0, "F2"), (1.0, "F3"), (1.0, "F4"), (1.0, "F5"), (1.0, "F6"), (1.0, "F7"), (1.0, "F8"), (1.0, "F9"), (1.0, "F10"), (1.0, "F11"), (1.0, "F12"), (1.0, "del"), (1.0, "") },
        new[] { (1.0, "`"), (1.0, "1"), (1.0, "2"), (1.0, "3"), (1.0, "4"), (1.0, "5"), (1.0, "6"), (1.0, "7"), (1.0, "8"), (1.0, "9"), (1.0, "0"), (1.0, "-"), (1.0, "="), (2.0, "backspace") },
        new[] { (1.5, "tab"), (1.0, "Q"), (1.0, "W"), (1.0, "E"), (1.0, "R"), (1.0, "T"), (1.0, "Y"), (1.0, "U"), (1.0, "I"), (1.0, "O"), (1.0, "P"), (1.0, "["), (1.0, "]"), (1.5, "\\") },
        new[] { (1.75, "caps"), (1.0, "A"), (1.0, "S"), (1.0, "D"), (1.0, "F"), (1.0, "G"), (1.0, "H"), (1.0, "J"), (1.0, "K"), (1.0, "L"), (1.0, ";"), (1.0, "'"), (2.25, "enter") },
        new[] { (2.25, "shift"), (1.0, "Z"), (1.0, "X"), (1.0, "C"), (1.0, "V"), (1.0, "B"), (1.0, "N"), (1.0, "M"), (1.0, ","), (1.0, "."), (1.0, "/"), (2.75, "shift") },
        new[] { (1.0, "ctrl"), (1.0, "fn"), (1.0, "⊞"), (1.0, "alt"), (6.0, ""), (1.0, "alt"), (1.0, "ctrl"), (1.0, "‹"), (1.0, "˄˅"), (1.0, "›") },
    };

    protected override Size MeasureOverride(Size available)
    {
        double w = double.IsInfinity(available.Width) ? 560 : Math.Min(available.Width, 640);
        return new Size(w, HeightFor(w));
    }

    private static double HeightFor(double w) { double pad = w * 0.025, u = (w - 2 * pad) / 15.0; return pad * 2 + u * 0.55 + 5 * u; }

    protected override void OnRender(DrawingContext dc)
    {
        double W = ActualWidth, pad = W * 0.025, u = (W - 2 * pad) / 15.0, gap = u * 0.1, row = u, fnRow = u * 0.55;
        double H = pad * 2 + fnRow + 5 * row;
        double lvl = Math.Max(0, Math.Min(1, Level));
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

        dc.DrawRoundedRectangle(Deck, null, new Rect(0, 0, W, H), u * 0.35, u * 0.35);
        if (lvl > 0)
        {
            var under = new RadialGradientBrush(Color.FromArgb((byte)(lvl * 46), 255, 255, 255), Colors.Transparent) { RadiusX = 0.6, RadiusY = 0.7 };
            dc.DrawRoundedRectangle(under, null, new Rect(0, 0, W, H), u * 0.35, u * 0.35);
        }

        var edge = new Pen(new SolidColorBrush(Color.FromArgb((byte)(20 + lvl * 120), 255, 255, 255)), 1);
        edge.Freeze();
        var lit = new SolidColorBrush(Color.FromArgb((byte)(70 + lvl * 185), 255, 255, 255)); lit.Freeze();
        Brush legend = lvl > 0 ? lit : Dim;

        double y = pad;
        for (int r = 0; r < Rows.Length; r++)
        {
            double h = r == 0 ? fnRow : row, x = pad;
            foreach (var (w, l) in Rows[r])
            {
                var rect = new Rect(x + gap / 2, y + gap / 2, w * u - gap, h - gap);
                if (l == "˄˅")
                {
                    // half-height up/down arrows stacked in one slot
                    var top = new Rect(rect.X, rect.Y, rect.Width, rect.Height / 2 - gap / 4);
                    var bot = new Rect(rect.X, rect.Y + rect.Height / 2 + gap / 4, rect.Width, rect.Height / 2 - gap / 4);
                    DrawKey(dc, top, "˄", u, edge, legend, dpi);
                    DrawKey(dc, bot, "˅", u, edge, legend, dpi);
                }
                else DrawKey(dc, rect, l, u, edge, legend, dpi);
                x += w * u;
            }
            y += h;
        }
    }

    private void DrawKey(DrawingContext dc, Rect rect, string label, double u, Pen edge, Brush legend, double dpi)
    {
        dc.DrawRoundedRectangle(KeyFill, edge, rect, u * 0.12, u * 0.12);
        if (string.IsNullOrEmpty(label)) return;
        double size = label.Length > 1 && !label.StartsWith("˄") ? u * 0.2 : u * 0.3;
        var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(6, size), legend, dpi);
        dc.DrawText(ft, new Point(rect.X + (rect.Width - ft.Width) / 2, rect.Y + (rect.Height - ft.Height) / 2));
    }
}
