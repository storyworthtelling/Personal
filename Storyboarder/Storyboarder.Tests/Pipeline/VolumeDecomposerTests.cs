using Storyboarder.Domain.Pipeline;
using Xunit;

namespace Storyboarder.Tests.Pipeline;

// The pure page-to-volume decomposition: one volume per chapter, splitting a chapter that is too large
// for a single binding at scene boundaries (falling back to an even page split), and renumbering pages
// and volumes in reading order. Everything here is deterministic and needs no database or services.
public class VolumeDecomposerTests
{
    private static DecomposablePage Page(int chapter, string label = "", bool scene = false) =>
        new(Guid.NewGuid(), chapter, label, scene);

    private static List<DecomposablePage> Pages(params DecomposablePage[] pages) => [.. pages];

    [Fact]
    public void OneVolumePerChapter_WhenEveryChapterFitsTheCap()
    {
        var pages = Pages(
            Page(1, "Arrival"), Page(1, "Arrival"),
            Page(2, "The Road"), Page(2, "The Road"), Page(2, "The Road"));

        var plan = VolumeDecomposer.Decompose(pages, maxPagesPerVolume: 10);

        Assert.Equal(2, plan.Count);
        Assert.Equal(1, plan[0].Number);
        Assert.Equal("Arrival", plan[0].Title);
        Assert.Equal([pages[0].PageId, pages[1].PageId], plan[0].PageIds);
        Assert.Equal(2, plan[1].Number);
        Assert.Equal("The Road", plan[1].Title);
        Assert.Equal([pages[2].PageId, pages[3].PageId, pages[4].PageId], plan[1].PageIds);
    }

    [Fact]
    public void UsesNumberedTitle_WhenAChapterHasNoLabel()
    {
        var plan = VolumeDecomposer.Decompose(Pages(Page(3), Page(3)), maxPagesPerVolume: 10);

        var single = Assert.Single(plan);
        Assert.Equal("Chapter 3", single.Title);
    }

    [Fact]
    public void UndetectedChapters_BecomeOneUntitledVolume()
    {
        // No chapters detected: every page is chapter 0. Under the cap that is a single volume whose cover
        // shows just "Volume 1" (empty title), not "Chapter 0".
        var plan = VolumeDecomposer.Decompose(Pages(Page(0), Page(0), Page(0)), maxPagesPerVolume: 10);

        var single = Assert.Single(plan);
        Assert.Equal(1, single.Number);
        Assert.Equal("", single.Title);
    }

    [Fact]
    public void OversizedChapter_SplitsAtSceneBoundaries()
    {
        // A 10-page chapter, cap 5, with scene starts at pages index 4 and 7 (0-based). The greedy packer
        // cuts as late as it can without exceeding the cap, snapping each cut to a scene start, so the
        // volumes break between scenes rather than mid-scene: [0..4), [4..7), [7..10).
        var pages = new List<DecomposablePage>();
        for (int i = 0; i < 10; i++)
            pages.Add(Page(1, "Long Chapter", scene: i is 4 or 7));

        var plan = VolumeDecomposer.Decompose(pages, maxPagesPerVolume: 5);

        Assert.Equal(3, plan.Count);
        Assert.Equal([.. pages[..4].Select(p => p.PageId)], plan[0].PageIds);
        Assert.Equal([.. pages[4..7].Select(p => p.PageId)], plan[1].PageIds);
        Assert.Equal([.. pages[7..].Select(p => p.PageId)], plan[2].PageIds);
        Assert.Equal("Long Chapter · Part 1", plan[0].Title);
        Assert.Equal("Long Chapter · Part 2", plan[1].Title);
        Assert.Equal("Long Chapter · Part 3", plan[2].Title);
    }

    [Fact]
    public void OversizedChapter_FallsBackToEvenSplit_WhenNoSceneBoundaryIsInReach()
    {
        // No scene starts at all: the packer can only cut at the cap boundary, so parts are cap-sized.
        var pages = new List<DecomposablePage>();
        for (int i = 0; i < 12; i++)
            pages.Add(Page(1, "Unbroken"));

        var plan = VolumeDecomposer.Decompose(pages, maxPagesPerVolume: 5);

        Assert.Equal([5, 5, 2], plan.Select(v => v.PageIds.Count).ToList());
    }

    [Fact]
    public void NoVolumeEverExceedsTheCap()
    {
        var pages = new List<DecomposablePage>();
        for (int i = 0; i < 100; i++)
            pages.Add(Page(1 + i / 9, scene: i % 3 == 0));

        var plan = VolumeDecomposer.Decompose(pages, maxPagesPerVolume: 7);

        Assert.All(plan, v => Assert.InRange(v.PageIds.Count, 1, 7));
    }

    [Fact]
    public void VolumesAreNumberedSequentially_AndEveryPageAppearsOnceInOrder()
    {
        var pages = Pages(
            Page(1, "One"),
            Page(2, "Two"), Page(2, "Two"), Page(2, "Two"), Page(2, "Two"), Page(2, "Two"), Page(2, "Two"),
            Page(3, "Three"));

        var plan = VolumeDecomposer.Decompose(pages, maxPagesPerVolume: 4);

        Assert.Equal([1, 2, 3, 4], plan.Select(v => v.Number).ToList());
        Assert.Equal(
            pages.Select(p => p.PageId).ToList(),
            plan.SelectMany(v => v.PageIds).ToList());
    }

    [Fact]
    public void EmptyInput_YieldsNoVolumes()
    {
        Assert.Empty(VolumeDecomposer.Decompose([], maxPagesPerVolume: 10));
    }
}
