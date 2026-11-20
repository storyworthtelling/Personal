namespace Watch.Domain.Design;

/// <summary>
/// An OKLCH colour as the screen draws it: packed ARGB (alpha in the top byte), each channel clipped to
/// sRGB. The one conversion every watch view uses, so a painter never holds a colour of its own (WD §13).
/// </summary>
public static class Srgb
{
    public static int Argb(Oklch colour, double alpha = 1)
    {
        var hue = colour.H * Math.PI / 180;
        var a = colour.C * Math.Cos(hue);
        var b = colour.C * Math.Sin(hue);

        var l = Cube(colour.L + 0.3963377774 * a + 0.2158037573 * b);
        var m = Cube(colour.L - 0.1055613458 * a - 0.0638541728 * b);
        var s = Cube(colour.L - 0.0894841775 * a - 1.2914855480 * b);

        var red = Channel(4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s);
        var green = Channel(-1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s);
        var blue = Channel(-0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
        var opacity = Byte(alpha);

        return unchecked((opacity << 24) | (red << 16) | (green << 8) | blue);
    }

    private static double Cube(double value) => value * value * value;

    private static int Channel(double linear)
    {
        var clipped = Math.Clamp(linear, 0, 1);
        var encoded = clipped <= 0.0031308 ? 12.92 * clipped : 1.055 * Math.Pow(clipped, 1 / 2.4) - 0.055;
        return Byte(encoded);
    }

    private static int Byte(double unit) => (int)Math.Round(Math.Clamp(unit, 0, 1) * 255);
}
