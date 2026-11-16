using Folio.Domain.Readiness;
using Folio.Domain.Report;
using Folio.Infrastructure.Imaging;
using SkiaSharp;

namespace Folio.Tests.Report;

/// <summary>
/// A note, its mark and its leader take their severity's colour (R8): Problem rose for a Must, Caution
/// ochre for a Should, muted ink for a Consider. The rM2 shows the report in greys, and there rose and
/// ochre are the same grey (measured: both near 0.41), so colour alone cannot carry the severity. Every
/// pair of the three has to differ in grey by a clear step or in its stroke.
/// </summary>
public class SeverityInGreyTests
{
    private static readonly ReadinessTier[] Tiers = [ReadinessTier.Must, ReadinessTier.Should, ReadinessTier.Consider];

    /// <summary>Two of the rM2's sixteen levels.</summary>
    private const double ClearStep = 2.0 / 15;

    [Fact]
    public void TheNotesTakeRoseOchreAndMutedInk()
    {
        Assert.Equal(ReportPalette.MustFix, ReportPalette.Severity(ReadinessTier.Must));
        Assert.Equal(ReportPalette.Should, ReportPalette.Severity(ReadinessTier.Should));
        Assert.Equal(ReportPalette.InkMuted, ReportPalette.Severity(ReadinessTier.Consider));
    }

    [Fact]
    public void EveryPairStaysDistinguishableInGreys()
    {
        foreach (var a in Tiers)
            foreach (var b in Tiers.Where(tier => tier > a))
            {
                var greys = Math.Abs(Grey(ReportPalette.Severity(a)) - Grey(ReportPalette.Severity(b)));
                var strokes = !SeverityCue.Leader(a).Dash.SequenceEqual(SeverityCue.Leader(b).Dash);
                Assert.True(greys >= ClearStep || strokes, $"{a} and {b} print as the same grey with the same stroke.");
            }
    }

    /// <summary>Each stays readable on the sheet: darker than the hairline by a clear step.</summary>
    [Fact]
    public void EachIsDarkerThanTheHairline()
    {
        foreach (var tier in Tiers)
            Assert.True(Grey(ReportPalette.Hairline) - Grey(ReportPalette.Severity(tier)) >= ClearStep, $"{tier} is too pale.");
    }

    /// <summary>The leader is heavier than the hairline it replaced, and a Must's is the heaviest.</summary>
    [Fact]
    public void TheLeaderIsHeavierThanAHairline()
    {
        Assert.All(Tiers, tier => Assert.True(SeverityCue.Leader(tier).Width > 0.6));
        Assert.True(SeverityCue.Leader(ReadinessTier.Must).Width > SeverityCue.Leader(ReadinessTier.Consider).Width);
    }

    private static double Grey(SKColor color) =>
        ((0.299 * color.Red) + (0.587 * color.Green) + (0.114 * color.Blue)) / 255.0;
}
