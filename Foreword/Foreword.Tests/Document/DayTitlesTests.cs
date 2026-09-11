using Foreword.Domain.Document;
using Xunit;

namespace Foreword.Tests.Document;

/// <summary>
/// What the document is called on the tablet, and — the part that would have gone wrong quietly —
/// what yesterday's was called.
///
/// <para>The read-back finds yesterday's sheets <b>by title</b>, downloads them, extracts the
/// handwriting, files it, and then trashes them. Collapsing two documents into one and naming the
/// result something new does not break that: it makes it find nothing. Yesterday's two documents
/// are still on the tablet under their old names, so the handwriting on them is never read, the
/// events written on them are never filed, and they are never trashed — they simply accumulate,
/// with no error anywhere, on the one morning the owner would least expect the app to have changed
/// its mind about what it is called.</para>
/// </summary>
public class DayTitlesTests
{
    private static readonly DateOnly Day = new(2026, 9, 13);

    [Fact]
    public void TheDocumentIsNamedForItsDay()
    {
        Assert.Equal("The Day 2026-09-13", DayTitles.Document(Day));
    }

    /// <summary>
    /// Sortable, so a tablet's own list puts a week of them in order. A "Sunday, September 13" file
    /// name sorts alphabetically, which puts April before January.
    /// </summary>
    [Fact]
    public void TheNameSortsByDate()
    {
        var names = new[] { Day.AddDays(20), Day, Day.AddDays(-40) }
            .Select(DayTitles.Document)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(
            [DayTitles.Document(Day.AddDays(-40)), DayTitles.Document(Day), DayTitles.Document(Day.AddDays(20))],
            names);
    }

    /// <summary>
    /// The read-back looks for the new name AND the two it replaced. This is the transition: the
    /// morning the merged document first goes out, what is on the tablet is yesterday's calendar and
    /// yesterday's checklist, under the names the old build gave them.
    /// </summary>
    [Fact]
    public void TheReadBackAlsoLooksForWhatTheOldBuildLeftBehind()
    {
        var titles = DayTitles.ToReadBack(Day);

        Assert.Contains("The Day 2026-09-13", titles);
        Assert.Contains("Daily Calendar 2026-09-13", titles);
        Assert.Contains("Daily Checklist 2026-09-13", titles);
    }

    /// <summary>
    /// The new one is looked for first. On an ordinary morning it is the only one that exists, and
    /// two failed lookups before the one that works is two round trips to a cloud service for
    /// nothing.
    /// </summary>
    [Fact]
    public void TheCurrentNameIsTriedFirst()
    {
        Assert.Equal(DayTitles.Document(Day), DayTitles.ToReadBack(Day)[0]);
    }

    /// <summary>
    /// The heading on page 1 is the date written out, which is a different job from the file name:
    /// one is read by a person on a page, the other is sorted by a machine in a list.
    /// </summary>
    [Fact]
    public void ThePageHeadingIsTheDateWrittenOut()
    {
        Assert.Equal("Sunday, September 13", DayTitles.PageHeading(Day));
    }
}
