using System.Globalization;

namespace Storyworthtelling.Paper;

/// <summary>
/// OKLCH → sRGB hex conversion (Björn Ottosson's OKLab). The P4 palette is generated systematically in
/// OKLCH (DESIGN.md §3/§19) so the tiers share perceptual lightness and the roles sit on an even hue wheel;
/// this is the math the generator runs. Channels are clamped into gamut (the palette is deliberately muted,
/// so clamping is a no-op in practice). Pure — no SkiaSharp dependency.
/// </summary>
public static class Oklch
{
    /// <summary>Convert an OKLCH triple (L 0..1, chroma C, hue in degrees) to an uppercase <c>#RRGGBB</c>.</summary>
    public static string ToHex(double l, double c, double hueDegrees)
    {
        var h = hueDegrees * Math.PI / 180.0;
        var a = c * Math.Cos(h);
        var b = c * Math.Sin(h);

        var lp = l + 0.3963377774 * a + 0.2158037573 * b;
        var mp = l - 0.1055613458 * a - 0.0638541728 * b;
        var sp = l - 0.0894841775 * a - 1.2914855480 * b;

        var l3 = lp * lp * lp;
        var m3 = mp * mp * mp;
        var s3 = sp * sp * sp;

        var r = 4.0767416621 * l3 - 3.3077115913 * m3 + 0.2309699292 * s3;
        var g = -1.2684380046 * l3 + 2.6097574011 * m3 - 0.3413193965 * s3;
        var bl = -0.0041960863 * l3 - 0.7034186147 * m3 + 1.7076147010 * s3;

        return "#" + Channel(r) + Channel(g) + Channel(bl);
    }

    private static string Channel(double linear)
    {
        var s = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        var v = (int)Math.Round(Math.Clamp(s, 0, 1) * 255);
        return v.ToString("X2", CultureInfo.InvariantCulture);
    }
}
