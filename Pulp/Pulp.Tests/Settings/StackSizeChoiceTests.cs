using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>D106: the stack's size is configurable, default three films and two shows, a card at a time.</summary>
public class StackSizeChoiceTests
{
    [Fact]
    public void TheHeaderReadsTheDefaultAsDrawn() =>
        Assert.Equal("3 films · 2 shows", StackSizeChoice.Summary(StackSizes.Default));

    [Theory]
    [InlineData(1, 1, "1 film · 1 show")]
    [InlineData(0, 2, "No films · 2 shows")]
    [InlineData(4, 0, "4 films · No shows")]
    public void TheHeaderCountsEachPile(int films, int shows, string expected) =>
        Assert.Equal(expected, StackSizeChoice.Summary(new StackSizes(films, shows)));

    [Fact]
    public void AStepChangesOnlyItsPile()
    {
        var more = StackSizeChoice.Step(StackSizes.Default, TitleKind.Film, +1);

        Assert.Equal(4, more.Films);
        Assert.Equal(2, more.Shows);
    }

    [Fact]
    public void APileStopsAtTheMost()
    {
        var full = new StackSizes(StackSizeChoice.Most, 2);

        Assert.False(StackSizeChoice.CanStep(full, TitleKind.Film, +1));
        Assert.Throws<ArgumentOutOfRangeException>(() => StackSizeChoice.Step(full, TitleKind.Film, +1));
        Assert.True(StackSizeChoice.CanStep(full, TitleKind.Film, -1));
    }

    [Fact]
    public void APileCanBeEmptiedButNeverBelowNone()
    {
        var noShows = new StackSizes(3, 0);

        Assert.False(StackSizeChoice.CanStep(noShows, TitleKind.Show, -1));
        Assert.True(StackSizeChoice.CanStep(new StackSizes(3, 1), TitleKind.Show, -1));
    }

    /// <summary>A stack of nothing is no stack: the last card cannot be taken away.</summary>
    [Fact]
    public void TheLastCardStays()
    {
        var one = new StackSizes(1, 0);

        Assert.False(StackSizeChoice.CanStep(one, TitleKind.Film, -1));
        Assert.True(StackSizeChoice.CanStep(one, TitleKind.Show, +1));
    }

    [Fact]
    public void AKindWithNoPileOrAZeroStepCannotStep()
    {
        Assert.False(StackSizeChoice.CanStep(StackSizes.Default, (TitleKind)99, +1));
        Assert.False(StackSizeChoice.CanStep(StackSizes.Default, TitleKind.Film, 0));
    }

    [Theory]
    [InlineData(null, null, 3, 2)]
    [InlineData(5, 1, 5, 1)]
    [InlineData(0, 0, 3, 2)]
    [InlineData(-1, 2, 3, 2)]
    [InlineData(4, 99, 3, 2)]
    [InlineData(2, null, 3, 2)]
    public void AStoredSizeOutOfBoundsReadsAsTheDefault(int? films, int? shows, int expectFilms, int expectShows)
    {
        var read = StackSizeChoice.Read(films, shows);

        Assert.Equal(expectFilms, read.Films);
        Assert.Equal(expectShows, read.Shows);
    }
}
