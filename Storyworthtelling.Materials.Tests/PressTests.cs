namespace Storyworthtelling.Materials.Tests;

/// <summary>DESIGN.md §2's pressed row: while held, a surface's own grain or tooth rises by half; no colour changes.</summary>
public class PressTests
{
    [Fact]
    public void PressingAPencilTakesHalfAgainOfWhatItsToothTook()
    {
        var rest = PencilTooth.Build();
        var held = rest.Lifted(MaterialStrength.PressLift);

        for (var i = 0; i < rest.Values.Length; i++)
        {
            var lost = 255 - rest.Values[i];
            var expected = Math.Max(0, 255 - (int)Math.Round(lost * (1 + MaterialStrength.PressLift)));
            Assert.Equal(expected, held.Values[i]);
        }
    }

    [Fact]
    public void PressingTheGrainDeepensEachFleckByHalf()
    {
        var held = PaperGrainTile.Build().Grained(MaterialStrength.PressLift);

        Assert.All(held.Values, value => Assert.True(value is 0 or 48));
    }

    [Fact]
    public void APressedMarkerStreaksHalfAgainAsMuch()
    {
        var rest = Streaks.Marker();
        var held = Streaks.Marker(MaterialStrength.PressLift);

        for (var i = 0; i < rest.Length; i++)
        {
            Assert.InRange(held[i] - (255 - (int)Math.Round((255 - rest[i]) * 1.5)), -1, 1);
        }
    }

    [Fact]
    public void LiftingNothingChangesNothing()
    {
        var tile = PencilTooth.Build();
        Assert.Equal(tile.Values, tile.Lifted(0).Values);
        Assert.Equal(Streaks.Marker(), Streaks.Marker(0));
    }
}
