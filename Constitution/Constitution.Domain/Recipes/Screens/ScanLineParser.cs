using System.Text.RegularExpressions;
using Constitution.Domain.Documents;

namespace Constitution.Domain.Recipes.Screens;

/// <summary>An ingredient line split as far as it could be read exactly. A line that could not be keeps all of itself in <see cref="Name"/>.</summary>
public sealed record ScanIngredientDraft(string? Quantity, string? Unit, string Name);

/// <summary>A step line, whole, and the minutes it states if it states them once and plainly.</summary>
public sealed record ScanStepDraft(string Text, int? Minutes);

/// <summary>
/// Reads a scanned line into the card's parts (C-6.5.3). The rule is the scaler's own: read only what is exact and leave the rest
/// in the words, because a figure the parser guessed at would be read at the counter as a fact.
///
/// <para><b>Ingredients.</b> A leading quantity is taken only when the committed <see cref="ServingsScale"/> reads it exactly (a
/// whole number, a fraction glyph, "1 1/2", a decimal); a range ("1-2", "2 to 3"), a word ("a pinch") or anything else stays in the
/// name. A unit is taken only from a short list of kitchen units. <b>Steps.</b> Minutes are read from "N min" or "N minutes" only
/// when that is the line's one time: two times, a range, or any mention of hours leaves the minutes out. Whether the cook is busy
/// for the time is not something a line says, so it is never marked active here.</para>
/// </summary>
public static class ScanLineParser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly HashSet<string> Units = new(StringComparer.OrdinalIgnoreCase)
    {
        "cup", "cups", "tbsp", "tbs", "tablespoon", "tablespoons", "tsp", "teaspoon", "teaspoons", "oz", "ounce", "ounces",
        "lb", "lbs", "pound", "pounds", "g", "gram", "grams", "kg", "ml", "l", "liter", "liters", "litre", "litres",
        "pint", "pints", "quart", "quarts", "can", "cans", "clove", "cloves", "pinch", "pinches", "stick", "sticks",
        "slice", "slices", "bunch", "bunches", "package", "packages", "sprig", "sprigs", "stalk", "stalks", "head", "heads",
        "fillet", "fillets", "piece", "pieces",
    };

    private static readonly HashSet<string> RangeWords = new(StringComparer.OrdinalIgnoreCase) { "to", "or", "and", "x", "×", "-", "–", "—" };

    private static readonly Regex Spaces = new(@"\s+", RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Minutes = new(@"(?<![\w.])(\d{1,5})\s*(?:minutes?|mins?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex Hours = new(@"\b(?:hours?|hrs?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex MinuteRange = new(@"\d\s*(?:-|–|—|to|or)\s*\d{1,5}\s*(?:minutes?|mins?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);

    public static ScanIngredientDraft Ingredient(string? line)
    {
        var text = Spaces.Replace((line ?? string.Empty).Trim(), " ");
        var whole = new ScanIngredientDraft(null, null, text);
        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // The quantity may be two tokens ("1 1/2", "1 ½"); try the longer reading first.
        foreach (var take in new[] { 2, 1 })
        {
            if (tokens.Length <= take)
            {
                continue;
            }

            var quantity = string.Join(' ', tokens[..take]);
            if (!IsExactQuantity(quantity))
            {
                continue;
            }

            var rest = tokens[take..];
            if (StartsAnotherQuantity(rest[0]))
            {
                return whole;
            }

            string? unit = null;
            var maybeUnit = rest[0].TrimEnd('.');
            if (Units.Contains(maybeUnit))
            {
                unit = maybeUnit;
                rest = rest[1..];
                if (rest.Length > 1 && rest[0].Equals("of", StringComparison.OrdinalIgnoreCase))
                {
                    rest = rest[1..];
                }
            }

            return rest.Length == 0 ? whole : new ScanIngredientDraft(quantity, unit, string.Join(' ', rest));
        }

        return whole;
    }

    public static ScanStepDraft Step(string? line)
    {
        var text = (line ?? string.Empty).Trim();
        return new ScanStepDraft(text, MinutesIn(text));
    }

    private static int? MinutesIn(string text)
    {
        var found = Minutes.Matches(text);
        if (found.Count != 1 || Hours.IsMatch(text) || MinuteRange.IsMatch(text))
        {
            return null;
        }

        var minutes = int.Parse(found[0].Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        return minutes is >= 1 and <= RecipeStep.MaxMinutes ? minutes : null;
    }

    /// <summary>A plain number the scaler reads exactly: it starts with a digit or fraction glyph, has no letters, and scales.</summary>
    private static bool IsExactQuantity(string candidate)
    {
        if (candidate.Any(char.IsLetter) || !StartsWithNumber(candidate))
        {
            return false;
        }

        return ServingsScale.Scale(candidate, 1, 2).Kind == ScaleKind.Scaled;
    }

    private static bool StartsAnotherQuantity(string token) =>
        RangeWords.Contains(token) || token.StartsWith('-') || token.StartsWith('–') || token.StartsWith('—') || StartsWithNumber(token);

    private static bool StartsWithNumber(string token) =>
        token.Length > 0 && (char.IsDigit(token[0]) || token[0] == '.' || char.GetUnicodeCategory(token[0]) == System.Globalization.UnicodeCategory.OtherNumber);
}
