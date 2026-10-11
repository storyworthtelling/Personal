using System.Globalization;

namespace Pulp.Domain.Discover;

/// <summary>
/// One page of search or mood results: the household's own titles, then — after the hairline — the
/// ones elsewhere, the count line, and whether "more" has anything behind it.
/// </summary>
public sealed record DiscoverResultPage(
    IReadOnlyList<DiscoverRow> Yours,
    IReadOnlyList<DiscoverRow> Elsewhere,
    string CountLine,
    bool HasMore)
{
    public bool IsEmpty => Yours.Count == 0 && Elsewhere.Count == 0;
}

/// <summary>
/// Search and mood results (D172; no mockup, D272): yours first and elsewhere after the hairline, each
/// in the order the source gave; the top twenty, with "more" adding twenty at a time; the count line
/// reads matches of total. The cut is taken after ordering, so a title on the household's services is
/// never pushed off the page by twenty that are not.
/// </summary>
public static class DiscoverPageResults
{
    public const int PageSize = 20;

    public const string MoreLabel = "More";

    public static DiscoverResultPage Build(IEnumerable<DiscoverRow> rows, int pages)
    {
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pages);

        var distinct = rows.DistinctBy(row => row.Id).ToList();
        var ordered = distinct.Where(row => row.OnYourServices).Concat(distinct.Where(row => !row.OnYourServices)).ToList();
        var shown = ordered.Take(PageSize * pages).ToList();

        return new DiscoverResultPage(
            shown.Where(row => row.OnYourServices).ToList(),
            shown.Where(row => !row.OnYourServices).ToList(),
            string.Create(CultureInfo.InvariantCulture, $"{shown.Count} of {ordered.Count}"),
            shown.Count < ordered.Count);
    }
}
