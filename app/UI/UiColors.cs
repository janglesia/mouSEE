using System.Windows.Media;

namespace app.UI;

// The two colours the calibration screens are built from, and which one the middle dot (MiddleDot) is: the yellow on
// a dark background, the dark on a yellow one, so it always looks like the rest of the calibration.
public static class UiColors
{
    /// <summary>The calibration yellow: dots, progress, the grid screen's background.</summary>
    public static readonly Color Yellow = Hex("#F7D060");

    /// <summary>The dark of the calibration screens' background.</summary>
    public static readonly Color Dark = Hex("#101010");

    /// <summary>The middle dot's colour on this background: whichever of the yellow and the dark stands out more from it.</summary>
    public static Color MiddleDot(Color background) => Contrast(Yellow, background) >= Contrast(Dark, background) ? Yellow : Dark;

    /// <summary>WCAG contrast ratio, 1 (same) to 21 (black on white).</summary>
    public static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(byte v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    public static Color Hex(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
