using Folio.Domain.Analysis;
using Folio.Domain.Readiness;
using Folio.Domain.Report;
using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Tests.Report;

/// <summary>
/// G18 — a finding recolours the run of the line it covers, and the chart emits no filled series.
///
/// <para>The bug this reintroduces is drawing the four severities as filled bands, which is the
/// obvious way to show "this stretch is a problem" and is unreadable on paper. The measurement is
/// below: the Document profile's four severity tones are equiluminant, so the edge between two
/// adjacent fills carries about 1.02:1 and exists in hue alone.</para>
///
/// <para>The second bug it reintroduces is letting the first finding along a stretch own it, so a
/// Must lying under a Consider prints as the milder of the two.</para>
/// </summary>
public class PacingChartTests
{
    private static readonly IReadOnlyList<ChapterTension> Curve =
    [
        new("1", 0.6), new("2", 0.5), new("3", 0.3), new("4", 0.2),
        new("5", 0.3), new("6", 0.7), new("7", 0.8), new("8", 0.9),
    ];

    [Fact]
    public void AFindingOverARangeRecoloursThatRunOfTheLine()
    {
        var overlay = PacingChart.Overlay(Curve, [new PacingFinding(2, 4, ReadinessTier.Must)]);

        var marked = Assert.Single(overlay.Runs, run => run.Severity == ReadinessTier.Must);
        Assert.Equal(2, marked.FromChapter);
        Assert.Equal(4, marked.ToChapter);
    }

    [Fact]
    public void ASingleChapterFindingIsADotRatherThanARun()
    {
        var overlay = PacingChart.Overlay(Curve, [new PacingFinding(5, 5, ReadinessTier.Should)]);

        var dot = Assert.Single(overlay.Dots);
        Assert.Equal(5, dot.Chapter);
        Assert.All(overlay.Runs, run => Assert.Null(run.Severity));
    }

    /// <summary>
    /// The runs tile the whole curve. A gap would print as a break in the line and an overlap would
    /// print one severity over another, both of which look like a rendering fault rather than a
    /// reading.
    /// </summary>
    [Fact]
    public void TheRunsTileTheWholeCurveWithNoGapAndNoOverlap()
    {
        var overlay = PacingChart.Overlay(Curve,
        [
            new PacingFinding(1, 3, ReadinessTier.Should),
            new PacingFinding(5, 7, ReadinessTier.Consider),
        ]);

        Assert.Equal(0, overlay.Runs[0].FromChapter);
        Assert.Equal(Curve.Count - 1, overlay.Runs[^1].ToChapter);
        for (var i = 1; i < overlay.Runs.Count; i++)
        {
            Assert.Equal(overlay.Runs[i - 1].ToChapter, overlay.Runs[i].FromChapter);
        }
    }

    /// <summary>Where two findings lie on the same stretch, the worse one owns it.</summary>
    [Fact]
    public void TheWorseFindingOwnsAStretchTwoOfThemShare()
    {
        var overlay = PacingChart.Overlay(Curve,
        [
            new PacingFinding(1, 5, ReadinessTier.Consider),
            new PacingFinding(2, 4, ReadinessTier.Must),
        ]);

        var over3 = Assert.Single(overlay.Runs,
            run => run.FromChapter <= 2 && run.ToChapter >= 3 && run.Severity == ReadinessTier.Must);
        Assert.Equal(ReadinessTier.Must, over3.Severity);
    }

    [Fact]
    public void ASagTakesTheHueOfTheFindingSittingOnIt()
    {
        var overlay = PacingChart.Overlay(Curve, [new PacingFinding(2, 4, ReadinessTier.Must)]);

        var sag = Assert.Single(overlay.Sags);
        Assert.Equal(ReadinessTier.Must, sag.Severity);
    }

    /// <summary>A sag nobody remarked on is still drawn — and says so by carrying no severity.</summary>
    [Fact]
    public void ASagNoReaderRemarkedOnCarriesNoSeverity()
    {
        var overlay = PacingChart.Overlay(Curve, []);

        Assert.Null(Assert.Single(overlay.Sags).Severity);
    }

    /// <summary>
    /// Nothing the chart emits is a filled series. Structural, because the pull toward a filled band
    /// is a rendering decision made in a renderer, and this is the only place it can be refused
    /// before it is made.
    /// </summary>
    [Fact]
    public void TheChartEmitsNoFilledSeries()
    {
        var members = new[] { typeof(PacingOverlay), typeof(TensionRun), typeof(SagBand) }
            .SelectMany(type => type.GetProperties())
            .Select(property => property.Name)
            .ToList();

        Assert.DoesNotContain(members, name =>
            name.Contains("Fill", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Area", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Band", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Sags", StringComparison.Ordinal));
    }

    /// <summary>
    /// Why it is not fills, measured rather than remembered: the Document profile's four severity
    /// tones sit within a hair of the same luminance, so the edge where two filled bands meet is
    /// invisible to anyone who cannot separate the hues. This pins the reason so the decision is not
    /// re-taken by someone looking at a colour screen.
    /// </summary>
    [Fact]
    public void TheFourSeveritiesAreEquiluminantSoAFilledEdgeWouldVanish()
    {
        var tones = new[] { ColorRole.Problem, ColorRole.Caution, ColorRole.Reference, ColorRole.Positive }
            .Select(role => Luminance(PaperPalette.ResolveRole(role, ColorTier.Fill, UiColorProfile.DocumentColor)))
            .OrderBy(luminance => luminance)
            .ToList();

        for (var i = 1; i < tones.Count; i++)
        {
            var contrast = (tones[i] + 0.05) / (tones[i - 1] + 0.05);
            Assert.True(contrast < 1.1,
                $"Two severity fills now differ by {contrast:F2}:1. If the Document palette has "
                + "gained real separation the fills decision can be re-argued — but on the evidence "
                + "it was taken on, the boundary was invisible.");
        }
    }

    /// <summary>
    /// §16's non-colour cue: four severities on one line need four strokes that can be told apart
    /// with the colour thrown away, and all four must differ from the plain line as well.
    /// </summary>
    [Fact]
    public void EverySeverityCarriesItsOwnNonColourCue()
    {
        var strokes = Enum.GetValues<ReadinessTier>()
            .Select(SeverityCue.Stroke)
            .Append(SeverityCue.Plain)
            .Select(stroke => $"{stroke.Width}:{string.Join(',', stroke.Dash)}")
            .ToList();

        Assert.Equal(strokes.Count, strokes.Distinct().Count());
    }

    [Fact]
    public void AnEmptyCurveDrawsNothingRatherThanThrowing()
    {
        var overlay = PacingChart.Overlay([], [new PacingFinding(0, 3, ReadinessTier.Must)]);

        Assert.Empty(overlay.Runs);
        Assert.Empty(overlay.Dots);
        Assert.Empty(overlay.Sags);
    }

    private static double Luminance(SKColor colour) =>
        (0.2126 * Channel(colour.Red)) + (0.7152 * Channel(colour.Green)) + (0.0722 * Channel(colour.Blue));

    private static double Channel(byte value)
    {
        var scaled = value / 255.0;
        return scaled <= 0.03928 ? scaled / 12.92 : Math.Pow((scaled + 0.055) / 1.055, 2.4);
    }
}
