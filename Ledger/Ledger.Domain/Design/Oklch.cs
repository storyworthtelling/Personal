namespace Index.Domain.Design;

/// <summary>
/// OKLCH ↔ sRGB hex conversion (Björn Ottosson's OKLab), ported from
/// <c>Storyworthtelling.Paper/Oklch.cs</c> rather than referenced from it: that project pulls in
/// SkiaSharp/PDFsharp/<c>Storyworthtelling.Remarkable</c> (see its own csproj), and
/// <c>Index.Domain</c> stays free of every such dependency per this repo's architecture rules —
/// the same reasoning INDEX-P11-PLAN.md gives for reusing <c>Tools/IconStudio/Tokens.cs</c>'s hex
/// values as literals rather than a project reference. <see cref="Storyworthtelling.Paper.Oklch"/>
/// itself is dependency-free (its own doc comment says so), so this is a straight copy of that
/// math, not a re-derivation — a change to one should be checked against the other.
///
/// <para>Used by <see cref="PostItTint"/> to implement D127: a bank's Plaid <c>primary_color</c>
/// lightened to OKLCH L 0.94 with its chroma capped at 0.045.</para>
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

    /// <summary>Read an <c>#RRGGBB</c> back as an OKLCH triple — the inverse of <see cref="ToHex"/>.</summary>
    public static (double L, double C, double HueDegrees) FromHex(string hex)
    {
        var value = int.Parse(
            hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture);
        var r = Linear(((value >> 16) & 0xFF) / 255.0);
        var g = Linear(((value >> 8) & 0xFF) / 255.0);
        var b = Linear((value & 0xFF) / 255.0);

        var l = 0.4122214708 * r + 0.5363325363 * g + 0.0514459929 * b;
        var m = 0.2119034982 * r + 0.6806995451 * g + 0.1073969566 * b;
        var s = 0.0883024619 * r + 0.2817188376 * g + 0.6299787005 * b;

        var lp = Math.Cbrt(l);
        var mp = Math.Cbrt(m);
        var sp = Math.Cbrt(s);

        var okL = 0.2104542553 * lp + 0.7936177850 * mp - 0.0040720468 * sp;
        var okA = 1.9779984951 * lp - 2.4285922050 * mp + 0.4505937099 * sp;
        var okB = 0.0259040371 * lp + 0.7827717662 * mp - 0.8086757660 * sp;

        var chroma = Math.Sqrt(okA * okA + okB * okB);
        var hue = Math.Atan2(okB, okA) * 180.0 / Math.PI;
        if (hue < 0)
        {
            hue += 360.0;
        }
        return (okL, chroma, hue);
    }

    private static double Linear(double channel) =>
        channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);

    private static string Channel(double linear)
    {
        var s = linear <= 0.0031308 ? 12.92 * linear : 1.055 * Math.Pow(linear, 1.0 / 2.4) - 0.055;
        var v = (int)Math.Round(Math.Clamp(s, 0, 1) * 255);
        return v.ToString("X2", System.Globalization.CultureInfo.InvariantCulture);
    }
}
