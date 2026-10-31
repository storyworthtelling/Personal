using System.Text.RegularExpressions;

namespace Constitution.Domain.Documents;

/// <summary>
/// The "Have out" list (book p.8), derived rather than typed: a card lists its ingredients and steps,
/// and the pan the steps call for is in the steps. Each rule names a word the steps use and the thing to
/// put on the counter; the order is the table's, so the list reads the same every night, and anything
/// the card names that no step mentions follows it. An oven is assumed and never listed.
/// </summary>
public static class RecipeEquipment
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    private static readonly (Regex Words, string Item)[] Rules =
    [
        (Words(@"sheet[- ]pan|baking sheet|rimmed"), "Large rimmed sheet pan"),
        (Words(@"chop|halve|dice|slice|mince|cut|peel|trim"), "Chef’s knife and board"),
        (Words(@"bowl|toss|whisk"), "Large bowl"),
        (Words(@"thermometer|at the thickest"), "Instant-read thermometer"),
        (Words(@"skillet|frying pan|sauté|saute"), "Large skillet"),
        (Words(@"saucepan"), "Medium saucepan"),
        (Words(@"pot|stockpot|boil"), "Large pot"),
        (Words(@"dutch oven"), "Dutch oven"),
        (Words(@"slow cooker"), "Slow cooker"),
        (Words(@"blender|purée|puree"), "Blender"),
        (Words(@"colander|drain"), "Colander"),
    ];

    public static IReadOnlyList<string> From(
        IEnumerable<string> stepTexts, IEnumerable<RecipeIngredient> ingredients, IEnumerable<string> extra)
    {
        ArgumentNullException.ThrowIfNull(stepTexts);
        ArgumentNullException.ThrowIfNull(ingredients);
        ArgumentNullException.ThrowIfNull(extra);

        // Steps first; an ingredient's own words ("diced onion") are a cut already made, not a knife to find.
        var text = string.Join(' ', stepTexts);
        var have = Rules.Where(rule => rule.Words.IsMatch(text)).Select(rule => rule.Item).ToList();

        foreach (var item in extra.Select(e => e.Trim()).Where(e => e.Length > 0))
        {
            if (!have.Contains(item, StringComparer.OrdinalIgnoreCase))
            {
                have.Add(item);
            }
        }

        return have;
    }

    private static Regex Words(string alternatives) =>
        new(@"(?<![\p{L}])(?:" + alternatives + @")(?![\p{L}])", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, Timeout);
}
