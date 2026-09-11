using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// The segmented control's rule (DESIGN.md §8). A switch says on or off and leaves the owner
/// remembering which way round it was written; a segmented control says "Print" and "Pause".
/// </summary>
public class SegmentedChoiceTests
{
    private static SegmentedChoice PrintOrPause(string? picked = null) =>
        new(["Print", "Pause"], picked);

    [Fact]
    public void ExactlyOneOptionIsPicked()
    {
        var choice = PrintOrPause("Pause");

        Assert.Equal(1, choice.Options.Count(choice.IsPicked));
        Assert.Equal("Pause", choice.Picked);
    }

    /// <summary>
    /// A value nobody recognises falls back to the first rather than leaving nothing picked. A
    /// control showing no answer reads as one nobody has set, and this is a setting that always has
    /// a value — a stored option removed in a later build must not make the screen look unanswered.
    /// </summary>
    [Fact]
    public void AnUnrecognisedValueFallsBackRatherThanPickingNothing()
    {
        var choice = PrintOrPause("Maybe");

        Assert.Equal("Print", choice.Picked);
        Assert.Equal(1, choice.Options.Count(choice.IsPicked));
    }

    [Fact]
    public void NothingStoredPicksTheFirst()
    {
        Assert.Equal("Print", PrintOrPause().Picked);
    }

    /// <summary>
    /// Picking reports whether anything changed, because there is no Save on these screens: a
    /// caller commits on a real change, rather than storing the same value again every time someone
    /// taps the option already picked.
    /// </summary>
    [Fact]
    public void PickingTheSameOptionChangesNothing()
    {
        var choice = PrintOrPause("Print");

        Assert.False(choice.Pick("Print"));
        Assert.True(choice.Pick("Pause"));
        Assert.Equal("Pause", choice.Picked);
    }

    [Fact]
    public void PickingSomethingThatIsNotAnOptionIsIgnored()
    {
        var choice = PrintOrPause("Print");

        Assert.False(choice.Pick("Sometimes"));
        Assert.Equal("Print", choice.Picked);
    }

    /// <summary>A control with no options is not a control, and says so rather than drawing nothing.</summary>
    [Fact]
    public void ASegmentedControlNeedsOptions()
    {
        Assert.Throws<ArgumentException>(() => new SegmentedChoice([]));
    }

    [Fact]
    public void ThePickedIndexIsWhereTheViewDrawsTheHighlight()
    {
        Assert.Equal(1, PrintOrPause("Pause").PickedIndex);
    }
}
