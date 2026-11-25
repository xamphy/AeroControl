using System.Windows;
using System.Windows.Media;

namespace AeroControl.Themes;

/// <summary>Swaps the palette brushes in place, so every DynamicResource updates live.</summary>
public static class ThemeManager
{
    public static void Apply(bool dark, string accentHex)
    {
        var r = Application.Current.Resources;
        void Set(string key, string hex) => r[key] = Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));

        if (dark)
        {
            Set("Bg", "#1C1C1E"); Set("Side", "#232325"); Set("Card", "#2A2A2D"); Set("Field", "#3A3A3D");
            Set("Line", "#14FFFFFF"); Set("Text", "#F5F5F7"); Set("Sub", "#A1A1A6"); Set("Off", "#4A4A4E");
            Set("Seg", "#636366"); Set("Panel", "#F228282B"); Set("Tile", "#12FFFFFF"); Set("TileOn", "#24FFFFFF");
        }
        else
        {
            Set("Bg", "#F5F5F7"); Set("Side", "#EAEAEC"); Set("Card", "#FFFFFF"); Set("Field", "#E3E3E6");
            Set("Line", "#17000000"); Set("Text", "#1D1D1F"); Set("Sub", "#5E5E63"); Set("Off", "#D1D1D6");
            Set("Seg", "#FFFFFF"); Set("Panel", "#F5F6F6F8"); Set("Tile", "#0D000000"); Set("TileOn", "#FFFFFFFF");
        }
        var accent = (Color)ColorConverter.ConvertFromString(accentHex);
        r["Accent"] = Freeze(new SolidColorBrush(accent));
        r["AccentSoft"] = Freeze(new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B)));
    }

    private static SolidColorBrush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
