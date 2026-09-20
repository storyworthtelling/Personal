using System.Globalization;

namespace Folio.Domain.Shelf;

/// <summary>
/// The lines the book page sets beside a cover: where the book stands, how many drafts there have
/// been, and when the last one arrived.
///
/// <para>They live here for the same reason the shelf's geometry does. Spelling the small numbers
/// out is a frame decision — "three books" on a shelf, "three drafts" on a page — and a second copy
/// of it in a second screen is a copy that will one day say "3".</para>
/// </summary>
public static class BookLines
{
    /// <summary>
    /// Where this book stands: its place in its series, or — for a standalone, which has no place to
    /// stand in — what state it is in.
    /// </summary>
    public static string Standing(bool inSeries, int sequence, int seriesLength, string stage)
    {
        if (inSeries)
        {
            return seriesLength > 1 ? $"Book {sequence} of {Spell(seriesLength)}" : $"Book {sequence}";
        }

        return stage switch
        {
            "Completed" => "Reviewed",
            "Failed" or "Cancelled" => "Stopped",
            _ => "Being read",
        };
    }

    public static string Drafts(int count, DateTimeOffset? latest)
    {
        if (count <= 0)
            return string.Empty;

        var drafts = count == 1 ? $"{Capitalized(Spell(1))} draft" : $"{Capitalized(Spell(count))} drafts";
        return latest is null ? drafts : $"{drafts} · latest {Day(latest.Value)}";
    }

    /// <summary>
    /// The frame's own way with small numbers: words to ten, figures after.
    ///
    /// <para>Ten rather than a rounder-sounding six because the drawings settle it: the report's own
    /// pages say "Seven readers" and "Reader 3 of seven", and a document that spells three and
    /// prints 7 in the same sentence reads as two documents. Past ten the word is longer than the
    /// thing it counts and stops helping.</para>
    /// </summary>
    public static string Spell(int count) => count switch
    {
        1 => "one",
        2 => "two",
        3 => "three",
        4 => "four",
        5 => "five",
        6 => "six",
        7 => "seven",
        8 => "eight",
        9 => "nine",
        10 => "ten",
        _ => count.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>A date at the length a phone line has: "12 Sep". The year is the book's, not the line's.</summary>
    private static string Day(DateTimeOffset when) =>
        when.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture);

    private static string Capitalized(string word) =>
        word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..];
}
