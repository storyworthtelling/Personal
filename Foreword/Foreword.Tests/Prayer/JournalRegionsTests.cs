using Foreword.Domain.Prayer;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The journal page has two writable regions, and both ends have to agree on where they are.
///
/// <para>Foreword drew the page, so it knows the coordinates (D17) — and it has to, because the
/// transcription is told which region is which rather than asked to work it out. A line written
/// under "Add to the list" becomes a request on tomorrow's list; the same line written two inches
/// higher is a journal entry. Get the boundary wrong and the two swap silently: nothing errors, and
/// a private thought turns up on the printed list.</para>
/// </summary>
public class JournalRegionsTests
{
    private const float Width = 612f;
    private const float Height = 792f;

    [Fact]
    public void ThereAreTwoRegionsAndTheyAreNamed()
    {
        var regions = JournalRegions.For(Width, Height);

        Assert.Equal(
            [JournalRegions.Journalling, JournalRegions.Adding],
            regions.Select(region => region.Name));
    }

    /// <summary>
    /// They must not overlap. An overlap is the one failure that cannot be seen on the sheet and
    /// cannot be seen in the transcription either — it is a band of the page that is both things.
    /// </summary>
    [Fact]
    public void TheyDoNotOverlap()
    {
        var regions = JournalRegions.For(Width, Height);

        var journalling = regions[0];
        var adding = regions[1];

        Assert.True(journalling.Top + journalling.Height <= adding.Top,
            $"the journalling region runs to {journalling.Top + journalling.Height} and adding starts at {adding.Top}");
    }

    /// <summary>
    /// The add strip is at the <b>foot</b>, under the free space rather than over it. Writing runs
    /// downward, so a strip at the top would be the first thing the pen meets and would collect
    /// whatever was on the owner's mind rather than what they meant to add.
    /// </summary>
    [Fact]
    public void TheAddStripIsAtTheFoot()
    {
        var regions = JournalRegions.For(Width, Height);

        Assert.True(regions[1].Top > Height / 2);
        Assert.True(regions[0].Height > regions[1].Height,
            "journalling should be the larger of the two — it is the page's purpose");
    }

    [Fact]
    public void BothSitInsideTheSheetAndClearOfTheTabs()
    {
        var regions = JournalRegions.For(Width, Height);

        foreach (var region in regions)
        {
            Assert.True(region.Left >= 0);
            Assert.True(region.Top >= 0);
            Assert.True(region.Left + region.Width <= Width - Domain.Document.SectionTabs.TabWidth);
            Assert.True(region.Top + region.Height <= Height);
            Assert.True(region.Height > 0);
        }
    }

    /// <summary>
    /// The add strip holds a definite number of lines, because the transcription is told how many
    /// to expect. "However many fitted" is not a thing a prompt can be told.
    /// </summary>
    [Fact]
    public void TheAddStripHoldsTheLinesItSaysItHolds()
    {
        var regions = JournalRegions.For(Width, Height);

        Assert.True(
            regions[1].Height >= JournalRegions.AddLines * JournalRegions.RuleSpacing,
            "the add strip is shorter than the lines it claims to hold");
    }
}
