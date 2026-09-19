using Codex.Domain.Music;
using Xunit;

namespace Codex.Tests.Music;

public class RescanOutcomeTests
{
    [Fact]
    public void None_IsEmpty()
    {
        Assert.True(RescanOutcome.None.IsEmpty);
    }

    [Fact]
    public void AnyNonZeroCount_IsNotEmpty()
    {
        Assert.False(new RescanOutcome(Added: 1, Changed: 0, Removed: 0).IsEmpty);
        Assert.False(new RescanOutcome(Added: 0, Changed: 1, Removed: 0).IsEmpty);
        Assert.False(new RescanOutcome(Added: 0, Changed: 0, Removed: 1).IsEmpty);
    }

    [Fact]
    public void Combine_AddsEachCount()
    {
        var combined = new RescanOutcome(1, 2, 3).Combine(new RescanOutcome(4, 5, 6));

        Assert.Equal(new RescanOutcome(5, 7, 9), combined);
    }

    [Fact]
    public void Combine_WithNone_IsUnchanged()
    {
        var outcome = new RescanOutcome(2, 0, 1);

        Assert.Equal(outcome, outcome.Combine(RescanOutcome.None));
    }

    [Fact]
    public void Describe_WhenEmpty_SaysNothingNew()
    {
        var description = RescanOutcome.None.Describe();

        Assert.Contains("nothing new", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_MentionsOnlyNonZeroCounts()
    {
        var description = new RescanOutcome(Added: 3, Changed: 0, Removed: 0).Describe();

        Assert.Contains("3 new track", description);
        Assert.DoesNotContain("changed", description, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("removed", description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Describe_SingularForOne()
    {
        var description = new RescanOutcome(Added: 1, Changed: 0, Removed: 0).Describe();

        Assert.Contains("1 new track", description);
        Assert.DoesNotContain("1 new tracks", description);
    }

    [Fact]
    public void Describe_MentionsAllThreeWhenAllNonZero()
    {
        var description = new RescanOutcome(Added: 2, Changed: 3, Removed: 1).Describe();

        Assert.Contains("2 new tracks", description);
        Assert.Contains("3 changed tracks", description);
        Assert.Contains("1 removed track", description);
    }
}
