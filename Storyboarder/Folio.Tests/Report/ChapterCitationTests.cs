using Folio.Domain.Analysis;
using Folio.Domain.Report;

namespace Folio.Tests.Report;

/// <summary>
/// The other half of G18: a finding only recolours a run of the line if the citation it arrived with
/// can be found on the chart at all.
///
/// <para>The bug this reintroduces is reading the first number and stopping — "Ch. 12-15" then
/// becomes a point at chapter 12, every range finding prints as a dot, and the run colouring that
/// F39 exists for never happens on a real report. Nothing fails; the chart just quietly goes back to
/// being a plain tension line with some dots on it.</para>
/// </summary>
public class ChapterCitationTests
{
    private static readonly IReadOnlyList<ChapterTension> Curve =
        [.. Enumerable.Range(1, 20).Select(n => new ChapterTension($"Chapter {n}", 0.5))];

    [Theory]
    [InlineData("Ch. 12-15", 11, 14)]
    [InlineData("Ch. 12–15", 11, 14)]      // en dash, which is what a model writing prose returns
    [InlineData("Ch. 12 — 15", 11, 14)]
    [InlineData("Chapters 3 to 5", 2, 4)]
    [InlineData("Chapter 7", 6, 6)]
    [InlineData("Ch. 7 · Scene 3", 6, 6)]  // a scene citation is still one chapter
    public void ACitationFindsTheChaptersItNames(string citation, int from, int to)
    {
        var range = ChapterCitation.Locate(citation, Curve);

        Assert.NotNull(range);
        Assert.Equal(from, range.Value.From);
        Assert.Equal(to, range.Value.To);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Throughout")]
    [InlineData("The ending")]
    public void ACitationNamingNoChapterIsNotPlacedOnTheChart(string citation)
    {
        Assert.Null(ChapterCitation.Locate(citation, Curve));
    }

    /// <summary>
    /// A citation past the end of the curve is dropped rather than clamped to the last chapter. A
    /// mark on chapter 20 that the model meant for chapter 40 is a lie about where the problem is,
    /// and the reader has no way to tell it from a real one.
    /// </summary>
    [Fact]
    public void ACitationOffTheEndOfTheCurveIsDroppedRatherThanClamped()
    {
        Assert.Null(ChapterCitation.Locate("Ch. 40", Curve));
    }

    /// <summary>A range that runs off the end keeps the part that is on the chart.</summary>
    [Fact]
    public void ARangeRunningPastTheEndKeepsWhatIsOnTheChart()
    {
        var range = ChapterCitation.Locate("Ch. 18-40", Curve);

        Assert.NotNull(range);
        Assert.Equal(17, range.Value.From);
        Assert.Equal(19, range.Value.To);
    }

    /// <summary>A curve labelled with bare numbers is the same chart.</summary>
    [Fact]
    public void TheCurvesOwnLabelsDoNotHaveToSayTheWordChapter()
    {
        IReadOnlyList<ChapterTension> bare = [.. Enumerable.Range(1, 8).Select(n => new ChapterTension($"{n}", 0.5))];

        var range = ChapterCitation.Locate("Ch. 3-5", bare);

        Assert.NotNull(range);
        Assert.Equal(2, range.Value.From);
        Assert.Equal(4, range.Value.To);
    }

    /// <summary>
    /// A book whose chapters are named rather than numbered has nothing to match against, so nothing
    /// is placed — which is the chart the report drew before F39, not a failure.
    /// </summary>
    [Fact]
    public void ACurveOfNamedChaptersPlacesNothing()
    {
        IReadOnlyList<ChapterTension> named =
            [new("The road out", 0.4), new("Low water", 0.3), new("What the dog knew", 0.6)];

        Assert.Null(ChapterCitation.Locate("Ch. 2", named));
    }

    [Theory]
    [InlineData("Must", Folio.Domain.Readiness.ReadinessTier.Must)]
    [InlineData("must", Folio.Domain.Readiness.ReadinessTier.Must)]
    [InlineData("Should", Folio.Domain.Readiness.ReadinessTier.Should)]
    [InlineData("Consider", Folio.Domain.Readiness.ReadinessTier.Consider)]
    [InlineData("anything else", Folio.Domain.Readiness.ReadinessTier.Consider)]
    public void APrioritysWordBecomesItsTier(string priority, Folio.Domain.Readiness.ReadinessTier expected)
    {
        Assert.Equal(expected, ChapterCitation.Tier(priority));
    }
}
