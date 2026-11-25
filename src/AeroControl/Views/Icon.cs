using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace AeroControl.Views;

/// <summary>A 24×24 stroke icon from an SVG-style path string, drawn at any size.</summary>
public sealed class Icon : Viewbox
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(string), typeof(Icon), new PropertyMetadata(null, (d, e) => ((Icon)d).Build()));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(Icon), new PropertyMetadata(Brushes.White, (d, e) => ((Icon)d).Build()));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
        nameof(Thickness), typeof(double), typeof(Icon), new PropertyMetadata(2.0, (d, e) => ((Icon)d).Build()));

    public string Data { get => (string)GetValue(DataProperty); set => SetValue(DataProperty, value); }
    public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }

    public Icon() { Width = 16; Height = 16; Stretch = Stretch.Uniform; }

    private void Build()
    {
        if (string.IsNullOrEmpty(Data)) { Child = null; return; }
        var path = new Path
        {
            Data = Geometry.Parse(Data), Stroke = Stroke, StrokeThickness = Thickness, Fill = Brushes.Transparent,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        var c = new Canvas { Width = 24, Height = 24 };
        c.Children.Add(path);
        Child = c;
    }
}
