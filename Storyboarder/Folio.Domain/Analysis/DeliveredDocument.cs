namespace Folio.Domain.Analysis;

/// <summary>
/// What the finished document is called where the owner will look for it.
///
/// <para>One function for both targets, because they have to agree: a report that lands in Drive
/// under one name and on the reMarkable under another is two documents to the person filing them,
/// and two interpolated strings in two files drift without anything failing.</para>
///
/// <para><b>"Revision report", not "Analysis."</b> Analysis was the pre-pivot product's word for its
/// output. The rebrand swept the app's own identity and left the name of the thing it actually hands
/// over — which is the one part of it the owner reads, in a folder listing, a year later. Nothing
/// renames a document already delivered; each draft is named once, at delivery.</para>
/// </summary>
public static class DeliveredDocument
{
    private const string Kind = "Revision report";

    /// <summary>The document's name, for a target that takes a title rather than a file name.</summary>
    public static string Name(string? title, int draftNumber) =>
        $"{Safe(title)} — Draft {draftNumber} — {Kind}";

    /// <summary>The same name as a file.</summary>
    public static string FileName(string? title, int draftNumber) => $"{Name(title, draftNumber)}.pdf";

    /// <summary>
    /// A title that can be a file name. A slash in a book's title is not exotic — "Book One / Book
    /// Two" is a real way to title a volume — and on most filesystems it quietly becomes a folder;
    /// an empty one is not a file at all, and failing on it would fail after the whole review has
    /// been paid for.
    /// </summary>
    private static string Safe(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "Untitled";

        var cleaned = title.Trim();
        foreach (var illegal in new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' })
        {
            cleaned = cleaned.Replace(illegal.ToString(), "-");
        }

        return string.Join(" ", cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }
}
