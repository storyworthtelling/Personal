using System.Globalization;
using System.Text.RegularExpressions;

namespace Constitution.Domain.Documents;

/// <summary>How a quantity came through the scaler.</summary>
public enum ScaleKind
{
    /// <summary>Read exactly and multiplied.</summary>
    Scaled,

    /// <summary>Nothing to do: the servings did not change, or the line has no quantity.</summary>
    AsWritten,

    /// <summary>The scaler could not read it exactly, so it is exactly as the card has it and the page says so.</summary>
    NotScaled,
}

/// <summary>A quantity after scaling, as the page prints it, and how it came through.</summary>
public sealed record ScaledQuantity(string Text, ScaleKind Kind);

/// <summary>
/// Scales a quantity written as text ("¾ cup", "1 1/2 tsp", "0.75 lb") from the servings a card
/// makes to the servings at the table. The arithmetic is exact (whole numbers over whole numbers), the
/// result is written as a whole number and a friendly fraction, and <b>anything not read exactly is left
/// as the card has it</b>: a range ("1-2 tsp"), a word ("a pinch"), a second measurement inside the
/// line ("1 cup (240 ml)", which would go stale as the first number moved) or digits that do not
/// parse. A number the scaler guessed at would be read at the counter as a fact.
/// </summary>
public static class ServingsScale
{
    /// <summary>A result within this share of the nearest friendly fraction is written as that fraction.</summary>
    private const long CloseEnoughOneIn = 20;

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly (char Glyph, long Top, long Bottom)[] Fractions =
    [
        ('⅛', 1, 8), ('¼', 1, 4), ('⅓', 1, 3), ('⅜', 3, 8), ('½', 1, 2), ('⅝', 5, 8), ('⅔', 2, 3), ('¾', 3, 4), ('⅞', 7, 8),
    ];

    private static readonly Dictionary<string, string> Plurals = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cup"] = "cups", ["can"] = "cans", ["clove"] = "cloves", ["slice"] = "slices", ["stick"] = "sticks",
        ["bunch"] = "bunches", ["head"] = "heads", ["sprig"] = "sprigs", ["package"] = "packages", ["piece"] = "pieces",
        ["stalk"] = "stalks", ["fillet"] = "fillets", ["pinch"] = "pinches", ["pound"] = "pounds", ["ounce"] = "ounces",
        ["teaspoon"] = "teaspoons", ["tablespoon"] = "tablespoons", ["quart"] = "quarts", ["pint"] = "pints",
    };

    private static readonly string FractionClass = "[" + new string(Fractions.Select(f => f.Glyph).ToArray()) + "]";

    private static readonly Regex Number = new(
        @"^(?:(?<whole>[0-9]{1,6})\s*(?<glyph>" + FractionClass + @")"
        + @"|(?<mixed>[0-9]{1,6})\s+(?<mtop>[0-9]{1,6})/(?<mbottom>[0-9]{1,6})"
        + @"|(?<top>[0-9]{1,6})/(?<bottom>[0-9]{1,6})"
        + @"|(?<alone>" + FractionClass + @")"
        + @"|(?<decimal>[0-9]{0,6}\.[0-9]{1,6})"
        + @"|(?<integer>[0-9]{1,6}))(?<rest>.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline, Timeout);

    private static readonly Regex AnotherNumber = new(@"[0-9/" + new string(Fractions.Select(f => f.Glyph).ToArray()) + "]", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex RangeWord = new(@"^(?:[-–—×]|(?:to|or|and|x)\b)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex FirstWord = new(@"^([A-Za-z]+)(.*)$", RegexOptions.CultureInvariant | RegexOptions.Singleline, Timeout);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, Timeout);

    /// <summary>
    /// Thirds are written "1/3" and "2/3": Cascadia Code, the face the quantity column prints in, has no
    /// glyph for the vulgar thirds (it would print a box), though it reads them from a card.
    /// </summary>
    private static readonly Dictionary<char, string> WrittenAsText = new() { ['⅓'] = "1/3", ['⅔'] = "2/3" };

    /// <summary>Every non-ASCII character the scaler can write, so the printed face can be checked for them.</summary>
    public static IReadOnlyList<char> GlyphsUsed { get; } = Fractions.Select(f => f.Glyph).Where(g => !WrittenAsText.ContainsKey(g)).ToArray();

    public static ScaledQuantity Scale(string? quantity, int cardServings, int tableServings)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cardServings);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tableServings);

        var written = Spaces.Replace((quantity ?? string.Empty).Trim(), " ");
        if (written.Length == 0 || cardServings == tableServings)
        {
            return new ScaledQuantity(written, ScaleKind.AsWritten);
        }

        var asTheCardHasIt = new ScaledQuantity(written, ScaleKind.NotScaled);
        var match = Number.Match(written);
        if (!match.Success || !TryRead(match, out var top, out var bottom))
        {
            return asTheCardHasIt;
        }

        var rest = match.Groups["rest"].Value;
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return asTheCardHasIt;
        }

        rest = rest.Trim();
        if (AnotherNumber.IsMatch(rest) || RangeWord.IsMatch(rest))
        {
            return asTheCardHasIt;
        }

        var num = top * tableServings;
        var den = bottom * cardServings;
        if (num == 0)
        {
            return asTheCardHasIt;
        }

        var divisor = Gcd(num, den);
        num /= divisor;
        den /= divisor;

        if (!TryWrite(num, den, out var amount, out var plural))
        {
            return asTheCardHasIt;
        }

        var text = rest.Length == 0 ? amount : amount + " " + Agree(rest, plural);
        return new ScaledQuantity(text, ScaleKind.Scaled);
    }

    private static bool TryRead(Match match, out long top, out long bottom)
    {
        top = 0;
        bottom = 1;

        if (match.Groups["whole"].Success)
        {
            var (t, b) = FractionOf(match.Groups["glyph"].Value[0]);
            top = (Int(match, "whole") * b) + t;
            bottom = b;
        }
        else if (match.Groups["mixed"].Success)
        {
            bottom = Int(match, "mbottom");
            top = (Int(match, "mixed") * bottom) + Int(match, "mtop");
        }
        else if (match.Groups["top"].Success)
        {
            top = Int(match, "top");
            bottom = Int(match, "bottom");
        }
        else if (match.Groups["alone"].Success)
        {
            (top, bottom) = FractionOf(match.Groups["alone"].Value[0]);
        }
        else if (match.Groups["decimal"].Success)
        {
            var parts = match.Groups["decimal"].Value.Split('.');
            bottom = (long)Math.Pow(10, parts[1].Length);
            top = ((parts[0].Length == 0 ? 0 : long.Parse(parts[0], CultureInfo.InvariantCulture)) * bottom) + long.Parse(parts[1], CultureInfo.InvariantCulture);
        }
        else
        {
            top = Int(match, "integer");
        }

        return bottom > 0;
    }

    private static long Int(Match match, string group) => long.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);

    private static (long Top, long Bottom) FractionOf(char glyph)
    {
        var fraction = Fractions.First(f => f.Glyph == glyph);
        return (fraction.Top, fraction.Bottom);
    }

    /// <summary>The value as a whole number and a friendly fraction, or an honest two-place decimal when none is close.</summary>
    private static bool TryWrite(long num, long den, out string text, out bool plural)
    {
        var whole = num / den;
        var left = num % den;
        text = string.Empty;
        plural = false;

        if (left == 0)
        {
            text = whole.ToString(CultureInfo.InvariantCulture);
            plural = whole > 1;
            return true;
        }

        // Candidates: nothing, each friendly fraction, and the next whole number; nearest by exact cross-multiplication.
        var best = (Top: 0L, Bottom: 1L, Glyph: '\0');
        var bestGap = Gap(left, den, 0, 1);
        foreach (var (glyph, t, b) in Fractions.Append(('\0', 1L, 1L)))
        {
            var gap = Gap(left, den, t, b);
            if (CompareGaps(gap, bestGap) < 0)
            {
                best = (t, b, glyph);
                bestGap = gap;
            }
        }

        if (IsCloseEnough(bestGap, num, den))
        {
            if (best.Top == 1 && best.Bottom == 1)
            {
                text = (whole + 1).ToString(CultureInfo.InvariantCulture);
                plural = whole + 1 > 1;
            }
            else if (best.Top == 0)
            {
                text = whole.ToString(CultureInfo.InvariantCulture);
                plural = whole > 1;
            }
            else
            {
                var fraction = WrittenAsText.TryGetValue(best.Glyph, out var ascii) ? ascii : best.Glyph.ToString();
                var gap = whole > 0 && ascii is not null ? " " : string.Empty;
                text = (whole == 0 ? string.Empty : whole.ToString(CultureInfo.InvariantCulture)) + gap + fraction;
                plural = whole >= 1;
            }

            return true;
        }

        var rounded = Math.Round((decimal)num / den, 2, MidpointRounding.AwayFromZero);
        if (rounded <= 0m)
        {
            return false;
        }

        text = rounded.ToString("0.##", CultureInfo.InvariantCulture);
        plural = rounded > 1m;
        return true;
    }

    /// <summary>|left/den - t/b| as a fraction over (den * b).</summary>
    private static (long Top, long Bottom) Gap(long left, long den, long t, long b) => (Math.Abs((left * b) - (t * den)), den * b);

    private static int CompareGaps((long Top, long Bottom) a, (long Top, long Bottom) b) => (a.Top * b.Bottom).CompareTo(b.Top * a.Bottom);

    /// <summary>The gap is at most one part in twenty of the whole value num/den.</summary>
    private static bool IsCloseEnough((long Top, long Bottom) gap, long num, long den) => gap.Top * den * CloseEnoughOneIn <= gap.Bottom * num;

    private static long Gcd(long a, long b) => b == 0 ? a : Gcd(b, a % b);

    /// <summary>"cup" becomes "cups" above one and "cups" becomes "cup" at one or below; only the units listed, only the first word.</summary>
    private static string Agree(string rest, bool plural)
    {
        var word = FirstWord.Match(rest);
        if (!word.Success)
        {
            return rest;
        }

        var name = word.Groups[1].Value;
        var singular = Plurals.FirstOrDefault(p => p.Key.Equals(name, StringComparison.OrdinalIgnoreCase) || p.Value.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (singular.Key is null)
        {
            return rest;
        }

        var chosen = plural ? singular.Value : singular.Key;
        if (char.IsUpper(name[0]))
        {
            chosen = char.ToUpperInvariant(chosen[0]) + chosen[1..];
        }

        return chosen + word.Groups[2].Value;
    }
}
