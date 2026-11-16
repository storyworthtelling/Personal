using Folio.Domain.Analysis;
using Folio.Domain.Report;
using Folio.Domain.Text;

namespace Folio.Tests.Report;

/// <summary>
/// The pacing chart as the owner asked for it (R17, R38): only where a book has chapters enough to
/// trend across, chapter numbers along the bottom, and each point labelled with what drives or drains it
/// — the labels never printed over one another.
/// </summary>
public class PacingLabelsTests
{
    [Theory]
    [InlineData(StoryLength.Novel, 2, true)]
    [InlineData(StoryLength.Novella, 3, true)]
    [InlineData(StoryLength.Novella, 2, false)]
    [InlineData(StoryLength.ShortStory, 6, false)]
    [InlineData(StoryLength.Novel, 1, false)]
    public void TheChartIsForNovelsAndNovellasOfThreeChapters(StoryLength length, int chapters, bool shows)
    {
        Assert.Equal(shows, PacingChart.Shows(length, chapters));
    }

    [Theory]
    [InlineData("Chapter 7", 0, "7")]
    [InlineData("Ch. 12-15", 3, "12")]
    [InlineData("The Lighthouse", 4, "5")]
    [InlineData("", 0, "1")]
    public void EachPointIsNumberedByItsChapter(string chapter, int index, string label)
    {
        Assert.Equal(label, PacingChart.AxisLabel(new ChapterTension(chapter, 0.5), index));
    }

    /// <summary>Twenty chapters' labels, each wider than the gap between points, still never overlap.</summary>
    [Fact]
    public void CrowdedLabelsNeverOverlap()
    {
        var points = Enumerable.Range(0, 20).Select(i => new LabelRequest(i * 25.0, 60.0)).ToList();

        var placed = ChartLabels.Place(points, height: 9, left: 0, right: 475);

        Assert.Equal(points.Count, placed.Count);
        for (var a = 0; a < placed.Count; a++)
            for (var b = a + 1; b < placed.Count; b++)
                Assert.False(placed[a].Overlaps(placed[b]), $"Labels {a} and {b} overlap.");
    }

    [Fact]
    public void ALabelSitsOverItsOwnPointAndInsideTheChart()
    {
        var points = new[] { new LabelRequest(2, 60), new LabelRequest(240, 60), new LabelRequest(470, 60) };

        var placed = ChartLabels.Place(points, height: 9, left: 0, right: 475);

        for (var i = 0; i < points.Length; i++)
        {
            Assert.InRange(points[i].X, placed[i].Left, placed[i].Left + placed[i].Width);
            Assert.InRange(placed[i].Left, 0, 475 - placed[i].Width);
        }
    }

    /// <summary>Labels with room take the first lane, so a sparse chart is one row of words.</summary>
    [Fact]
    public void LabelsWithRoomShareTheFirstLane()
    {
        var points = new[] { new LabelRequest(0, 40), new LabelRequest(100, 40), new LabelRequest(200, 40) };

        var placed = ChartLabels.Place(points, height: 9, left: 0, right: 475);

        Assert.All(placed, label => Assert.Equal(0, label.Lane));
        Assert.Equal(1, ChartLabels.Lanes(placed));
    }
}
