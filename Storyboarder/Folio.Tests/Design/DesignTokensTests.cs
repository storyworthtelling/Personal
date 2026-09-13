using Folio.Domain.Design;

namespace Folio.Tests.Design;

/// <summary>
/// What is left of this head's own design literals.
///
/// <para>The in-app palette and type ladder belong to the shared <c>Storyworthtelling.Maui</c> kit,
/// whose tests check them against <c>PaperColorTokens</c> by reflection — one copy, so it cannot
/// disagree with itself. Release E did the same for the printed side: colour through the Document
/// profile, geometry in points. What remains here is two recorded DESIGN.md facts that nothing
/// duplicates, and this is the guard that they stay facts rather than drifting into a third palette.</para>
/// </summary>
public class DesignTokensTests
{
    [Fact]
    public void MotionIsTwoDurationsAndAnUndoWindow()
    {
        Assert.True(DesignTokens.Motion.QuickMs < DesignTokens.Motion.StandardMs,
            "Quick is meant to be the shorter of the two.");
        Assert.True(DesignTokens.Motion.UndoWindowMs >= 5_000,
            "An undo window shorter than five seconds is not an undo, it is a flicker.");
    }

    [Fact]
    public void TheGrainIsCappedSoItCannotMuddyText()
    {
        // DESIGN.md §17: texture is always on, and capped so it never fails contrast.
        Assert.InRange(DesignTokens.Grain.Alpha, 1, 40);
        Assert.True(DesignTokens.Grain.Density > 1, "Every pixel carrying grain is not grain, it is a tint.");
    }

    /// <summary>
    /// The colours and the 150-DPI page block are gone, not merely unused. A leftover <c>Accent</c>
    /// holding the retired terracotta is exactly the sort of thing the next person reaches for.
    /// </summary>
    [Fact]
    public void NoPaletteOrPixelGeometrySurvivesHere()
    {
        var members = typeof(DesignTokens).GetMembers()
            .Select(member => member.Name).ToArray();

        Assert.DoesNotContain("Accent", members);
        Assert.DoesNotContain("Paper", members);
        Assert.DoesNotContain("Ink", members);
        Assert.DoesNotContain("Pdf", members);
    }
}
