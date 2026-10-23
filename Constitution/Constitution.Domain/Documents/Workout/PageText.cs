using System.Text.RegularExpressions;

namespace Constitution.Domain.Documents;

/// <summary>
/// Breaking prose into the lines a page prints. The domain has no font to measure with, so the limits
/// are characters per line, calibrated against the real faces and held to the column by a read-back
/// test (<c>WordsKeepInsideTheLeftColumn</c>): a limit that drifts from the glyphs fails there.
/// </summary>
internal static class PageText
{
    /// <summary>Pinyon Script on the Title rung runs about 7.1 points a character; 34 fills 241 of the 270 column.</summary>
    public const int VoiceLineChars = 34;

    /// <summary>Body text in the 270 point column.</summary>
    public const int BodyColumnChars = 50;

    /// <summary>The secondary sans in the 270 point column.</summary>
    public const int SmallColumnChars = 58;

    /// <summary>Body text across the 373 point measure of a cover.</summary>
    public const int CoverBodyChars = 62;

    /// <summary>The secondary sans across a cover.</summary>
    public const int CoverSmallChars = 80;

    /// <summary>An exercise's name in the cover's 170 point name column: the semibold runs about 5.1 points a character, so 30 leaves room for the wide letters.</summary>
    public const int CoverNameChars = 30;

    /// <summary>An exercise's load in the cover's 70 point load column: the secondary sans runs about 4.4 points a character.</summary>
    public const int CoverLoadChars = 15;

    private static readonly Regex SentenceEnd = new(@"(?<=[.!?])\s+", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    /// <summary>Greedy wrap on spaces; a word longer than the limit stands on a line of its own.</summary>
    public static IReadOnlyList<string> Wrap(string text, int maxChars)
    {
        var lines = new List<string>();
        var current = string.Empty;

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length == 0)
            {
                current = word;
            }
            else if (current.Length + 1 + word.Length <= maxChars)
            {
                current += " " + word;
            }
            else
            {
                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    /// <summary>Each sentence starts a line, as the book breaks the voiced line; a long sentence wraps within itself.</summary>
    public static IReadOnlyList<string> WrapBySentence(string text, int maxChars) =>
        SentenceEnd.Split(text.Trim()).SelectMany(sentence => Wrap(sentence, maxChars)).ToList();

    /// <summary>"left knee" becomes "Left knee".</summary>
    public static string Capitalise(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static readonly string[] SmallNumbers =
    [
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
        "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen", "twenty",
    ];

    /// <summary>The number as a word up to twenty, digits after: "ten days", "34 days".</summary>
    public static string Word(int number) =>
        number is >= 0 and <= 20 ? SmallNumbers[number] : number.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
