using Storyworthtelling.Maui.Layout;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The rule under a screen's in-page header.
///
/// <para>Foreword shipped its screens with no rule at all — a centred Display label and nothing
/// under it — so these pin the shape rather than only the numbers: two strokes, the faint one
/// behind, both rising to the right, and a rule that gets longer rather than fatter as the header
/// widens.</para>
/// </summary>
public class MarkerRuleTests
{
    [Fact]
    public void ThereAreTwoStrokesAndTheFaintOneIsDrawnFirst()
    {
        Assert.Equal(2, MarkerRule.Strokes.Count);

        var bleed = MarkerRule.Strokes[0];
        var stroke = MarkerRule.Strokes[1];

        Assert.True(bleed.Opacity < stroke.Opacity,
            "The first stroke is the bleed and must be the fainter one; drawn the other way round "
            + "it sits on top and greys the stroke it is supposed to sit under.");
        Assert.True(bleed.Width < stroke.Width);
    }

    /// <summary>
    /// The bleed sits BELOW the broad stroke — larger y is further down the rule — so the ink looks
    /// to have run downwards off a stroke drawn left to right.
    /// </summary>
    [Fact]
    public void TheBleedSitsUnderTheStrokeAlongItsWholeLength()
    {
        var bleed = MarkerRule.Strokes[0];
        var stroke = MarkerRule.Strokes[1];

        Assert.True(bleed.StartY > stroke.StartY);
        Assert.True(bleed.EndY > stroke.EndY);
    }

    [Fact]
    public void BothStrokesLiftToTheRight()
    {
        foreach (var stroke in MarkerRule.Strokes)
        {
            Assert.True(stroke.EndY < stroke.StartY,
                "A rule drawn quickly under a title lifts; a level one reads as a border.");
            Assert.Equal(0f, stroke.StartX);
            Assert.Equal(MarkerRule.DesignWidth, stroke.EndX);
        }
    }

    [Fact]
    public void ScalingSpansTheFullWidthWhateverItIs()
    {
        var scaled = MarkerRule.Scale(MarkerRule.Strokes[1], width: 363f, height: MarkerRule.HeightDp);

        Assert.Equal(0f, scaled.StartX);
        Assert.Equal(363f, scaled.EndX, 3);
    }

    /// <summary>
    /// The one that is easy to get wrong. A stroke width scaled by the width would make the rule
    /// under a tablet-width header a fat band; it follows the height, which does not change.
    /// </summary>
    [Fact]
    public void AWiderHeaderGetsALongerRuleNotAFatterOne()
    {
        var narrow = MarkerRule.Scale(MarkerRule.Strokes[1], width: 200f, height: MarkerRule.HeightDp);
        var wide = MarkerRule.Scale(MarkerRule.Strokes[1], width: 800f, height: MarkerRule.HeightDp);

        Assert.Equal(narrow.Width, wide.Width, 3);
        Assert.True(wide.EndX > narrow.EndX);
    }

    [Fact]
    public void AtItsOwnHeightTheStrokesKeepTheirDesignWidths()
    {
        foreach (var stroke in MarkerRule.Strokes)
        {
            var scaled = MarkerRule.Scale(stroke, width: MarkerRule.DesignWidth, height: MarkerRule.HeightDp);

            Assert.Equal(stroke.Width, scaled.Width, 3);
            Assert.Equal(stroke.StartY, scaled.StartY, 3);
        }
    }

    /// <summary>Opacity is a property of the ink, not of how big the header is.</summary>
    [Fact]
    public void ScalingNeverTouchesOpacity()
    {
        foreach (var stroke in MarkerRule.Strokes)
        {
            Assert.Equal(stroke.Opacity, MarkerRule.Scale(stroke, 999f, 40f).Opacity);
        }
    }

    [Fact]
    public void EveryStrokeStaysInsideTheRulesOwnHeight()
    {
        foreach (var stroke in MarkerRule.Strokes)
        {
            foreach (var y in new[] { stroke.StartY, stroke.Control1Y, stroke.Control2Y, stroke.EndY })
            {
                Assert.InRange(y, 0f, MarkerRule.HeightDp);
            }
        }
    }
}
