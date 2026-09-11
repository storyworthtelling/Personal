using Foreword.Domain.Brief;
using Xunit;

namespace Foreword.Tests.Nyt;

/// <summary>
/// The contents at the foot of the foreword page (B1).
///
/// <para>It is there so the brief says what it holds <b>and how long it runs</b> before the reader
/// turns a page — the two questions you ask of a stack of paper you have just been handed, and the
/// two a document with no contents makes you find out by reading it.</para>
/// </summary>
public class BriefContentsTests
{
    [Fact]
    public void EachSectionSaysHowManyAndHowLong()
    {
        var rows = BriefContents.Rows(
        [
            new BriefSection("The news", 8, 14),
            new BriefSection("Newsletters", 2, 4),
        ]);

        Assert.Equal("The news", rows[0].Name);
        Assert.Equal("8 stories · 14 pages", rows[0].Detail);
        Assert.Equal("2 · 4 pages", rows[1].Detail);
    }

    /// <summary>
    /// "1 story", not "1 stories". The count is only named where there is a word for it — every
    /// other section is a bare number, exactly as the mockup draws it.
    /// </summary>
    [Fact]
    public void OneStoryIsSingular()
    {
        var rows = BriefContents.Rows([new BriefSection("The news", 1, 2)]);

        Assert.Equal("1 story · 2 pages", rows[0].Detail);
    }

    [Fact]
    public void OnePageIsSingularToo()
    {
        var rows = BriefContents.Rows([new BriefSection("xkcd", 1, 1)]);

        Assert.Equal("1 · 1 page", rows[0].Detail);
    }

    /// <summary>
    /// A section with nothing in it is left off. A contents line reading "Newsletters 0 · 0 pages"
    /// is an entry for an absence, which is the opposite of what a contents page is for.
    /// </summary>
    [Fact]
    public void AnEmptySectionIsNotListed()
    {
        var rows = BriefContents.Rows(
            [new BriefSection("The news", 8, 14), new BriefSection("Newsletters", 0, 0)]);

        Assert.Single(rows);
    }

    /// <summary>
    /// A section that is pages without being a count of things — the crossword — says only its
    /// length. "2 · 2 pages" for a crossword would be counting crosswords, which nobody does.
    /// </summary>
    [Fact]
    public void ASectionWithNoCountSaysOnlyItsLength()
    {
        var rows = BriefContents.Rows([new BriefSection("The crossword", null, 2)]);

        Assert.Equal("2 pages", rows[0].Detail);
    }

    [Fact]
    public void NothingAtAllIsNoContents()
    {
        Assert.Empty(BriefContents.Rows([]));
    }
}
