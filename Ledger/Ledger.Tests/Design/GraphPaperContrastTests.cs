using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// Guard G11 (D122): the graph paper's heavier line (64dp, 14%) is the measured contrast CEILING —
/// muted ink still clears 3:1 and soft ink still clears 4.5:1 against it. This checks the actual
/// composited pixel (the identity ground colour blended over the paper background at
/// <see cref="MaterialTokens.GraphPaperMajorAlpha"/>), not the flat identity hex — a guard that only
/// checked the flat colour would be checking something the screen never draws.
/// </summary>
public class GraphPaperContrastTests
{
    // The identity ground colour the graph paper's lines are drawn in (D115/D117, LedgerAreas.cs's
    // registered ladder) and the paper background they sit on (Storyworthtelling.Maui's kit token
    // "Paper", PaperColorTokens.Paper). Both are reference values for this test's own arithmetic, not
    // a second place either is decided — see HexLiteralLeakTests' allow-list remark for this folder.
    private const string IdentityGroundHex = "#839372";
    private const string PaperBackgroundHex = "#FAF8F2";

    private const string MutedInkHex = "#8A8578";
    private const string SoftInkHex = "#6B6759";

    [Fact]
    public void HeavierLine_MutedInkClearsThreeToOne()
    {
        var composited = DesignTestHelpers.CompositeOver(
            IdentityGroundHex, PaperBackgroundHex, (double)MaterialTokens.GraphPaperMajorAlpha);

        var ratio = DesignTestHelpers.ContrastRatio(MutedInkHex, composited);
        Assert.True(ratio >= 3.0, $"Muted ink only clears {ratio:0.00}:1 against the heavier graph-paper line.");
    }

    [Fact]
    public void HeavierLine_SoftInkClearsFourPointFiveToOne()
    {
        var composited = DesignTestHelpers.CompositeOver(
            IdentityGroundHex, PaperBackgroundHex, (double)MaterialTokens.GraphPaperMajorAlpha);

        var ratio = DesignTestHelpers.ContrastRatio(SoftInkHex, composited);
        Assert.True(ratio >= 4.5, $"Soft ink only clears {ratio:0.00}:1 against the heavier graph-paper line.");
    }

    /// <summary>
    /// The reintroduced bug this guard exists to catch (the guard ledger's own wording): darken the
    /// 64dp line from 14% to 16% and muted ink should fall under 3:1. Asserting the FAILURE mode
    /// directly (rather than trusting the two tests above alone) is what makes this a contrast
    /// CEILING test rather than a floor test that happens to pass at 14%.
    /// </summary>
    [Fact]
    public void ADarkerLine_WouldDropMutedInkBelowItsFloor()
    {
        var tooDark = DesignTestHelpers.CompositeOver(IdentityGroundHex, PaperBackgroundHex, 0.16);
        var ratio = DesignTestHelpers.ContrastRatio(MutedInkHex, tooDark);
        Assert.True(ratio < 3.0, $"16% was expected to breach muted ink's 3:1 floor, but measured {ratio:0.00}:1.");
    }
}
