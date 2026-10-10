using Pulp.Domain.Core;
using Pulp.Domain.Library;
using Pulp.Domain.People;

namespace Pulp.Tests.Library;

/// <summary>
/// Whose private lists this phone shows (D97). Nothing in the household yet names the phone's own
/// viewer (that is Release D's Household group), so the only safe answer is the roster's one adult;
/// with no roster, or two adults to choose between, it refuses rather than guesses — a guess shows
/// one adult the other's private history.
/// </summary>
public class LibraryViewerTests
{
    private static readonly ViewerId Ash = new("ash-example");
    private static readonly ViewerId Rowan = new("rowan-example");
    private static readonly ViewerId Wren = new("wren-example");

    [Fact]
    public void NoRosterIsNobody()
    {
        var answer = LibraryViewer.Me(null);

        Assert.Equal(LibraryViewerStatus.NoRoster, answer.Status);
        Assert.Null(answer.Viewer);
    }

    [Fact]
    public void TheOneAdultIsThePhonesViewerEvenWithChildren()
    {
        var answer = LibraryViewer.Me(new HouseholdRoster([new(Ash, false), new(Wren, true)]));

        Assert.Equal(LibraryViewerStatus.Known, answer.Status);
        Assert.Equal(Ash, answer.Viewer);
    }

    [Fact]
    public void TwoAdultsAreNotGuessedBetween()
    {
        var answer = LibraryViewer.Me(new HouseholdRoster([new(Ash, false), new(Rowan, false), new(Wren, true)]));

        Assert.Equal(LibraryViewerStatus.Unmatched, answer.Status);
        Assert.Null(answer.Viewer);
    }

    [Fact]
    public void AChildOnlyRosterHasNoAdultToShow()
    {
        var answer = LibraryViewer.Me(new HouseholdRoster([new(Wren, true)]));

        Assert.Equal(LibraryViewerStatus.Unmatched, answer.Status);
    }

    [Fact]
    public void AnUnmatchedPhoneSaysSoInOneQuietSentence()
    {
        Assert.False(string.IsNullOrWhiteSpace(LibraryViewer.UnmatchedStatus));
    }
}
