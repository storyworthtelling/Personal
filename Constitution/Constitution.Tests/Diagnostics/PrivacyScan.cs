using System.Text.RegularExpressions;

namespace Constitution.Tests.Diagnostics;

/// <summary>
/// The scanner behind <see cref="DiagnosticsPrivacyTests"/>: reads a rendered export and names the
/// <i>kinds</i> of private thing in it (never printing the match, since a failing run must not become
/// a second place a reading is published).
///
/// <para>It is a shape detector, and says so: the household's names and dishes are a list it was
/// handed, and a reading is recognised by its unit, its decimal point or its length. The two lines the
/// export is allowed to hold digits on — the version line and a document's file name — are removed
/// first, by their exact shape, so a date of birth written anywhere else is still a date.</para>
/// </summary>
internal static class PrivacyScan
{
    private static readonly string[] HouseholdNames = ["Sam", "Lee", "Robin", "Juniper"];

    private static readonly string[] DishWords =
        ["Lentil", "soup", "stew", "curry", "chicken", "pasta", "salad", "rice", "Pad thai", "roast"];

    private static readonly Regex VersionLine = new(
        @"^Version: [1-9]\.\d{1,2}\.\d{1,4}$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly Regex DocumentLine = new(
        @"^\s*(workout|meals|digest)-\d{4}-\d{2}-\d{2}\.pdf\b", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    private static readonly (string Kind, Regex Pattern)[] Shapes =
    [
        ("forbidden word", new Regex(@"(?i)weight|kcal|calorie|birth|bpm|\bkg\b", RegexOptions.CultureInvariant)),
        ("reading with a unit", new Regex(@"(?i)\d(?:[.,]\d+)?\s?(?:kg|lbs?|bpm|kcal|cal|mg|g|mmhg|cm|%)(?![a-z])", RegexOptions.CultureInvariant)),
        ("decimal number", new Regex(@"(?<![\d.])\d+[.,]\d+(?![\d.])", RegexOptions.CultureInvariant)),
        ("ratio", new Regex(@"(?<!\d)\d{2,3}/\d{2,3}(?!\d)", RegexOptions.CultureInvariant)),
        ("date", new Regex(@"(?<!\d)\d{1,4}[-/.]\d{1,2}[-/.]\d{1,4}(?!\d)", RegexOptions.CultureInvariant)),
        ("long number", new Regex(@"\d{4,}", RegexOptions.CultureInvariant)),
    ];

    /// <summary>The kinds of private thing found, in a stable order; empty means the text is clean.</summary>
    public static IReadOnlyList<string> Find(string text)
    {
        var kinds = new List<string>();
        foreach (var name in HouseholdNames)
        {
            if (Regex.IsMatch(text, $@"(?i)(?<![A-Za-z]){Regex.Escape(name)}(?![A-Za-z])"))
            {
                kinds.Add("household name");
                break;
            }
        }

        if (DishWords.Any(word => Regex.IsMatch(text, $@"(?i)(?<![A-Za-z]){Regex.Escape(word)}(?![A-Za-z])")))
        {
            kinds.Add("dish");
        }

        var stripped = DocumentLine.Replace(VersionLine.Replace(text, string.Empty), string.Empty);
        foreach (var (kind, pattern) in Shapes)
        {
            if (pattern.IsMatch(stripped))
            {
                kinds.Add(kind);
            }
        }

        return kinds;
    }
}
