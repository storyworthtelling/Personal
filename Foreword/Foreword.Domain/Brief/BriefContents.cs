namespace Foreword.Domain.Brief;

/// <summary>
/// One part of the brief: what it is, how many things are in it, and how many pages it runs.
/// </summary>
/// <param name="Count">
/// How many things. Null for a section that is pages without being a count of anything — a
/// crossword is two pages, not two crosswords.
/// </param>
public readonly record struct BriefSection(string Name, int? Count, int Pages);

/// <summary>One line of the contents.</summary>
public readonly record struct ContentsRow(string Name, string Detail);

/// <summary>
/// The contents at the foot of the foreword page (B1).
///
/// <para>There so the brief says what it holds <b>and how long it runs</b> before the reader turns a
/// page — the two questions you ask of a stack of paper you have just been handed, and the two a
/// document with no contents makes you find out by reading it.</para>
/// </summary>
public static class BriefContents
{
    /// <summary>The only section whose count gets a word. Everywhere else a bare number reads fine.</summary>
    private const string Stories = "The news";

    public static IReadOnlyList<ContentsRow> Rows(IReadOnlyList<BriefSection> sections) =>
    [
        // A line reading "Newsletters 0 · 0 pages" is an entry for an absence, which is the opposite
        // of what a contents page is for.
        .. sections
            .Where(section => section.Pages > 0 && section.Count != 0)
            .Select(section => new ContentsRow(section.Name, Detail(section))),
    ];

    private static string Detail(BriefSection section)
    {
        var pages = section.Pages == 1 ? "1 page" : $"{section.Pages} pages";

        if (section.Count is not { } count)
        {
            return pages;
        }

        var things = string.Equals(section.Name, Stories, StringComparison.Ordinal)
            ? count == 1 ? "1 story" : $"{count} stories"
            : count.ToString();

        return $"{things} · {pages}";
    }
}
