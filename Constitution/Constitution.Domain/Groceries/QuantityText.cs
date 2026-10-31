using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Constitution.Domain.Documents;

namespace Constitution.Domain.Groceries;

/// <summary>
/// Reading and writing the amounts on an ingredient line as exact fractions, so lines can be added without a rounding error. Only
/// what <see cref="ServingsScale"/> reads exactly is read here ("¾", "1 1/2", "0.75"); a range, a word or anything else is
/// <b>not a number</b> and the line keeps its words. Writing goes back through <see cref="ServingsScale"/>, so the list and the
/// recipe page spell the same amount the same way.
/// </summary>
internal static class QuantityText
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly Dictionary<char, (int Top, int Bottom)> Glyphs = new()
    {
        ['⅛'] = (1, 8), ['¼'] = (1, 4), ['⅓'] = (1, 3), ['⅜'] = (3, 8), ['½'] = (1, 2), ['⅝'] = (5, 8), ['⅔'] = (2, 3), ['¾'] = (3, 4), ['⅞'] = (7, 8),
    };

    private static readonly string GlyphClass = "[" + new string(Glyphs.Keys.ToArray()) + "]";

    private static readonly Regex Shape = new(
        @"^(?:(?<whole>[0-9]{1,6})\s*(?<glyph>" + GlyphClass + ")"
        + @"|(?<mixed>[0-9]{1,6})\s+(?<mtop>[0-9]{1,6})/(?<mbottom>[0-9]{1,6})"
        + @"|(?<top>[0-9]{1,6})/(?<bottom>[0-9]{1,6})"
        + "|(?<alone>" + GlyphClass + ")"
        + @"|(?<decimal>[0-9]{0,6}\.[0-9]{1,6})"
        + @"|(?<integer>[0-9]{1,6}))$",
        RegexOptions.CultureInvariant, Timeout);

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, Timeout);

    public static string Tidy(string? text) => Spaces.Replace((text ?? string.Empty).Trim(), " ");

    /// <summary>The amount as an exact fraction, or false when the text is not a plain positive number.</summary>
    public static bool TryRead(string? text, out Fraction value)
    {
        value = default;
        var match = Shape.Match(Tidy(text));
        if (!match.Success)
        {
            return false;
        }

        BigInteger top;
        BigInteger bottom = 1;
        if (match.Groups["whole"].Success)
        {
            var (glyphTop, glyphBottom) = Glyphs[match.Groups["glyph"].Value[0]];
            top = (Number(match, "whole") * glyphBottom) + glyphTop;
            bottom = glyphBottom;
        }
        else if (match.Groups["mixed"].Success)
        {
            bottom = Number(match, "mbottom");
            top = (Number(match, "mixed") * bottom) + Number(match, "mtop");
        }
        else if (match.Groups["top"].Success)
        {
            top = Number(match, "top");
            bottom = Number(match, "bottom");
        }
        else if (match.Groups["alone"].Success)
        {
            var (glyphTop, glyphBottom) = Glyphs[match.Groups["alone"].Value[0]];
            top = glyphTop;
            bottom = glyphBottom;
        }
        else if (match.Groups["decimal"].Success)
        {
            var parts = match.Groups["decimal"].Value.Split('.');
            bottom = BigInteger.Pow(10, parts[1].Length);
            top = ((parts[0].Length == 0 ? BigInteger.Zero : BigInteger.Parse(parts[0], CultureInfo.InvariantCulture)) * bottom) + BigInteger.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        else
        {
            top = Number(match, "integer");
        }

        if (bottom.IsZero || top.IsZero)
        {
            return false;
        }

        value = new Fraction(top, bottom);
        return true;
    }

    /// <summary>
    /// The amount with its unit as it prints. <see cref="ServingsScale"/> does the writing (whole numbers and friendly fractions,
    /// the unit agreeing with the number), by scaling a one or a two by the fraction; the two stands in for a one when the
    /// fraction is itself one, because the scaler leaves an unchanged amount exactly as written, plural unit and all.
    /// </summary>
    public static string Write(Fraction value, string? unit)
    {
        var tail = string.IsNullOrWhiteSpace(unit) ? string.Empty : " " + unit.Trim();
        if (value.Top <= int.MaxValue && value.Bottom <= int.MaxValue)
        {
            var scaled = value.IsOne
                ? ServingsScale.Scale("2" + tail, 2, 1)
                : ServingsScale.Scale("1" + tail, (int)value.Bottom, (int)value.Top);
            if (scaled.Kind != ScaleKind.NotScaled)
            {
                return scaled.Text;
            }
        }

        // The scaler would not write it (a unit that holds a number, or a fraction beyond its reach): two honest decimal places.
        var hundredths = ((value.Top * 100) + (value.Bottom / 2)) / value.Bottom;
        return ((decimal)hundredths / 100m).ToString("0.##", CultureInfo.InvariantCulture) + tail;
    }

    private static BigInteger Number(Match match, string group) => BigInteger.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
}

/// <summary>A positive amount as a reduced fraction. Plain arithmetic on whole numbers, so adding two amounts is exact.</summary>
internal readonly record struct Fraction
{
    public Fraction(BigInteger top, BigInteger bottom)
    {
        var divisor = BigInteger.GreatestCommonDivisor(top, bottom);
        Top = top / divisor;
        Bottom = bottom / divisor;
    }

    public BigInteger Top { get; }

    public BigInteger Bottom { get; }

    public bool IsOne => Top == Bottom;

    public static Fraction operator +(Fraction left, Fraction right) =>
        new((left.Top * right.Bottom) + (right.Top * left.Bottom), left.Bottom * right.Bottom);

    public static Fraction operator *(Fraction left, Fraction right) => new(left.Top * right.Top, left.Bottom * right.Bottom);
}
