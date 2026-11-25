using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AeroControl.ViewModels;

namespace AeroControl.Views;

/// <summary>
/// Draggable 15-point fan curve (temperature → duty), drawn like the mockup.
/// Temperatures stay strictly increasing (point 0 is the EC's floor and keeps its temperature);
/// duty never decreases with temperature and never goes below MinPercent (fans fully off is untested on this EC).
/// A live dot marks where the current temperature sits on the curve. Raises Committed when a drag ends.
/// </summary>
public sealed class FanCurveEditor : FrameworkElement
{
    private const double TMin = 40, TMax = 95, PadL = 40, PadB = 22, PadT = 8, PadR = 8;
    public const int MinPercent = 20;

    public static readonly DependencyProperty PointsProperty = DependencyProperty.Register(
        nameof(Points), typeof(ObservableCollection<CurvePoint>), typeof(FanCurveEditor),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty LabelBrushProperty = DependencyProperty.Register(
        nameof(LabelBrush), typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty KnobFillProperty = DependencyProperty.Register(
        nameof(KnobFill), typeof(Brush), typeof(FanCurveEditor), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentTempProperty = DependencyProperty.Register(
        nameof(CurrentTemp), typeof(double), typeof(FanCurveEditor), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public double CurrentTemp { get => (double)GetValue(CurrentTempProperty); set => SetValue(CurrentTempProperty, value); }
    public static readonly DependencyProperty FanOffBelowProperty = DependencyProperty.Register(
        nameof(FanOffBelow), typeof(int), typeof(FanCurveEditor), new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));
    /// <summary>Fans are off below this temperature (0 = no zone): shaded, and the curve is drawn at 0% there.</summary>
    public int FanOffBelow { get => (int)GetValue(FanOffBelowProperty); set => SetValue(FanOffBelowProperty, value); }

    public ObservableCollection<CurvePoint> Points { get => (ObservableCollection<CurvePoint>)GetValue(PointsProperty); set => SetValue(PointsProperty, value); }
    public Brush Accent { get => (Brush)GetValue(AccentProperty); set => SetValue(AccentProperty, value); }
    public Brush LineBrush { get => (Brush)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush LabelBrush { get => (Brush)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush KnobFill { get => (Brush)GetValue(KnobFillProperty); set => SetValue(KnobFillProperty, value); }

    public event Action Committed;
    private int _drag = -1, _hover = -1;

    public FanCurveEditor() { Focusable = true; Cursor = Cursors.Arrow; }

    private Rect Plot => new(PadL, PadT, Math.Max(10, ActualWidth - PadL - PadR), Math.Max(10, ActualHeight - PadT - PadB));
    private Point ToScreen(CurvePoint p)
    {
        var r = Plot;
        double t = Math.Max(TMin, Math.Min(TMax, p.Temp));
        return new Point(r.Left + (t - TMin) / (TMax - TMin) * r.Width, r.Bottom - p.Percent / 100.0 * r.Height);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var r = Plot;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // hit-testable
        var grid = new Pen(LineBrush, 1);
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        foreach (var pct in new[] { 0, 50, 100 })
        {
            double y = r.Bottom - pct / 100.0 * r.Height;
            dc.DrawLine(grid, new Point(r.Left, y), new Point(r.Right, y));
            var ft = Text($"{pct}%", dpi);
            dc.DrawText(ft, new Point(0, y - ft.Height / 2));
        }
        foreach (var t in new[] { 40, 55, 70, 85, 95 })
        {
            double x = r.Left + (t - TMin) / (TMax - TMin) * r.Width;
            var ft = Text($"{t} °C", dpi);
            dc.DrawText(ft, new Point(Math.Min(r.Right - ft.Width, Math.Max(r.Left, x - ft.Width / 2)), r.Bottom + 6));
        }
        if (Points == null || Points.Count == 0) return;

        var pts = Points.Select(ToScreen).ToList();
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false, false);
            g.PolyLineTo(pts.Skip(1).Concat(new[] { new Point(r.Right, r.Top) }).ToList(), true, true);
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(pts[0].X, r.Bottom), true, true);
            g.PolyLineTo(pts.Concat(new[] { new Point(r.Right, r.Top), new Point(r.Right, r.Bottom) }).ToList(), false, true);
        }
        var soft = Accent.Clone(); soft.Opacity = 0.16;
        double offX = FanOffBelow > TMin ? r.Left + (Math.Min(TMax, FanOffBelow) - TMin) / (TMax - TMin) * r.Width : r.Left;
        if (offX > r.Left)
        {
            // the curve only counts from the threshold on; below it the fans are off
            dc.PushClip(new RectangleGeometry(new Rect(offX, 0, Math.Max(0, ActualWidth - offX), ActualHeight)));
            dc.DrawGeometry(soft, null, fill);
            dc.DrawGeometry(null, new Pen(Accent, 2.5) { LineJoin = PenLineJoin.Round }, line);
            dc.Pop();
            var zone = LabelBrush.Clone(); zone.Opacity = 0.08;
            dc.DrawRectangle(zone, null, new Rect(r.Left, r.Top, offX - r.Left, r.Height));
            dc.DrawGeometry(null, new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) },
                new LineGeometry(new Point(offX, r.Top), new Point(offX, r.Bottom)));
            dc.DrawLine(new Pen(Accent, 2.5), new Point(r.Left, r.Bottom - 1), new Point(offX, r.Bottom - 1));
            var off = Text($"Fans off below {FanOffBelow} °C", dpi);
            if (off.Width < offX - r.Left - 8) dc.DrawText(off, new Point(r.Left + (offX - r.Left - off.Width) / 2, r.Top + r.Height * 0.45));
            else dc.DrawText(off, new Point(offX + 6, r.Top + 4));
        }
        else
        {
            dc.DrawGeometry(soft, null, fill);
            dc.DrawGeometry(null, new Pen(Accent, 2.5) { LineJoin = PenLineJoin.Round }, line);
        }

        if (CurrentTemp > 0)
        {
            // duty the curve gives at the current temperature (the EC steps between points; draw on the line)
            double t = CurrentTemp, pct = Points[0].Percent;
            for (int i = 1; i < Points.Count; i++)
            {
                var a = Points[i - 1]; var b = Points[i];
                if (t >= b.Temp) { pct = b.Percent; continue; }
                if (t > a.Temp) pct = a.Percent + (b.Percent - a.Percent) * (t - a.Temp) / Math.Max(1, b.Temp - a.Temp);
                break;
            }
            if (t >= Points[Points.Count - 1].Temp) pct = Math.Max(pct, Points[Points.Count - 1].Percent);
            if (FanOffBelow > 0 && t < FanOffBelow) pct = 0;
            var at = new Point(r.Left + (Math.Max(TMin, Math.Min(TMax, t)) - TMin) / (TMax - TMin) * r.Width, r.Bottom - pct / 100.0 * r.Height);
            var dash = new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(dash, new Point(at.X, r.Bottom), at);
            var glow = Accent.Clone(); glow.Opacity = 0.3;
            dc.DrawEllipse(glow, null, at, 10, 10);
            dc.DrawEllipse(Brushes.White, new Pen(Accent, 2), at, 5, 5);
            var lbl = Text($"Now {t:0} °C", dpi, Accent);
            double lx = Math.Min(r.Right - lbl.Width, Math.Max(r.Left, at.X - lbl.Width / 2));
            dc.DrawText(lbl, new Point(lx, Math.Min(r.Bottom - lbl.Height - 4, at.Y + 12)));
        }

        for (int i = 0; i < pts.Count; i++)
        {
            double rad = i == _drag || i == _hover ? 7 : 5;
            if (pts[i].X < offX - 0.5 && i != _drag && i != _hover) { var faded = Accent.Clone(); faded.Opacity = 0.35; dc.DrawEllipse(KnobFill, new Pen(faded, 1.5), pts[i], 4, 4); continue; }
            dc.DrawEllipse(KnobFill, new Pen(Accent, 2), pts[i], rad, rad);
        }
        if (_drag >= 0 || _hover >= 0)
        {
            var p = Points[_drag >= 0 ? _drag : _hover];
            var tip = Text($"{(p.Temp < TMin ? "≤40" : p.Temp.ToString())} °C · {p.Percent}%", dpi, Accent);
            var at = pts[_drag >= 0 ? _drag : _hover];
            dc.DrawText(tip, new Point(Math.Min(r.Right - tip.Width, Math.Max(r.Left, at.X - tip.Width / 2)), Math.Max(0, at.Y - 26)));
        }
    }

    private FormattedText Text(string s, double dpi, Brush b = null) =>
        new(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), 11, b ?? LabelBrush, dpi);

    private int Hit(Point m)
    {
        if (Points == null) return -1;
        int best = -1; double bestD = 12;
        for (int i = 0; i < Points.Count; i++)
        {
            double d = (ToScreen(Points[i]) - m).Length;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _drag = Hit(e.GetPosition(this));
        if (_drag >= 0) { CaptureMouse(); Focus(); e.Handled = true; InvalidateVisual(); }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var m = e.GetPosition(this);
        if (_drag < 0)
        {
            int h = Hit(m);
            if (h != _hover) { _hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Arrow; InvalidateVisual(); }
            return;
        }
        var r = Plot;
        var p = Points[_drag];
        int pct = (int)Math.Round((r.Bottom - m.Y) / r.Height * 100);
        int lo = Math.Max(MinPercent, _drag > 0 ? Points[_drag - 1].Percent : 0);
        int hi = _drag < Points.Count - 1 ? Points[_drag + 1].Percent : 100;
        p.Percent = Math.Max(lo, Math.Min(hi, Math.Max(0, Math.Min(100, pct))));
        if (_drag > 0)
        {
            int t = (int)Math.Round(TMin + (m.X - r.Left) / r.Width * (TMax - TMin));
            int tlo = Points[_drag - 1].Temp + 1;
            int thi = _drag < Points.Count - 1 ? Points[_drag + 1].Temp - 1 : (int)TMax;
            p.Temp = Math.Max(Math.Max(tlo, (int)TMin), Math.Min(thi, t));
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_drag < 0) return;
        _drag = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
        Committed?.Invoke();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        if (_hover >= 0 && _drag < 0) { _hover = -1; InvalidateVisual(); }
    }
}
