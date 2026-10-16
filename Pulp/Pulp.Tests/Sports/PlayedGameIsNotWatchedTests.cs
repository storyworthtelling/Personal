using Pulp.Domain.Library;
using Pulp.Domain.Sports;
using static Pulp.Tests.Sports.InventedLeagues;

namespace Pulp.Tests.Sports;

/// <summary>
/// D312: a hand-off to a service says the service was opened, not that the game was watched. For a game the
/// hand-off is usage evidence only; a recording stays unwatched (and its league held) until the viewer marks it
/// watched or a session ends.
/// </summary>
public class PlayedGameIsNotWatchedTests
{
    private static readonly IReadOnlyCollection<TeamId> Followed = [Gulls.Id];

    private static WatchEvent Sitting(WatchSource evidence, bool ended = false) =>
        new(new Pulp.Domain.Core.TitleId(GullsRecording.Value), WatchedKind.Game, Now.AddHours(-1), null, null, evidence, Ended: ended);

    [Fact]
    public void AHandOffAloneLeavesTheRecordingUnwatched()
    {
        var unwatched = RecordedGames.Unwatched(CoastalFeed(), Followed, [Sitting(WatchSource.HandOff)], Now);

        Assert.Contains(GullsRecording, unwatched);
    }

    [Fact]
    public void AMarkWatchedStillClearsIt()
    {
        var unwatched = RecordedGames.Unwatched(CoastalFeed(), Followed, [Sitting(WatchSource.MarkedWatched)], Now);

        Assert.DoesNotContain(GullsRecording, unwatched);
    }

    [Fact]
    public void ASessionThatEndedStillClearsItEvenWithAHandOffBeforeIt()
    {
        var unwatched = RecordedGames.Unwatched(CoastalFeed(), Followed,
            [Sitting(WatchSource.HandOff), Sitting(WatchSource.Session, ended: true)], Now);

        Assert.DoesNotContain(GullsRecording, unwatched);
    }

    [Fact]
    public void ALeagueStaysHeldAfterTheHandOff()
    {
        var unwatched = RecordedGames.Unwatched(CoastalFeed(), Followed, [Sitting(WatchSource.HandOff)], Now);

        Assert.True(SpoilerHold.IsHeld(CoastalFeed(), Followed, unwatched, Now));
    }
}
