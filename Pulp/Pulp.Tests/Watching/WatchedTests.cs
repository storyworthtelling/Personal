using Pulp.Domain.Watching;

namespace Pulp.Tests.Watching;

/// <summary>
/// D10, D30, D182: what a viewer has watched is learned from Pulp's own use — the playing app's
/// media session first, the hand-off as the fallback — and a manual mark is always there beside it.
/// Times and positions here are invented.
/// </summary>
public class WatchedTests
{
    private static readonly DateTimeOffset Monday = new(2026, 3, 2, 20, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Episode = TimeSpan.FromMinutes(44);

    private static DateTimeOffset At(int minutes) => Monday.AddMinutes(minutes);

    private static SessionObservation Session(int minutes, double positionMinutes, bool ended = false) =>
        new(At(minutes), TimeSpan.FromMinutes(positionMinutes), Episode, ended);

    private static WatchedVerdict Decide(
        IEnumerable<HandedOff>? handOffs = null,
        IEnumerable<SessionObservation>? sessions = null,
        IEnumerable<ManualMark>? marks = null) =>
        Watched.Decide(new WatchEvidence(handOffs ?? [], sessions ?? [], marks ?? []));

    [Fact]
    public void NothingKnownIsNotWatched()
    {
        var verdict = Decide();

        Assert.False(verdict.IsWatched);
        Assert.Equal(WatchedReason.NoEvidence, verdict.Reason);
    }

    [Fact]
    public void AHandOffWithNoSessionIsAssumedWatched()
    {
        var verdict = Decide(handOffs: [new HandedOff(At(0))]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.HandedOff, verdict.Reason);
    }

    [Fact]
    public void ASessionThatReachesTheEndIsWatched()
    {
        var verdict = Decide(handOffs: [new HandedOff(At(0))], sessions: [Session(10, 5), Session(50, 44)]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.SessionReachedEnd, verdict.Reason);
    }

    [Fact]
    public void ASessionReportedAsEndedIsWatchedEvenWithoutAPosition()
    {
        // A live game's session has no meaningful duration; its end is the app saying so (D182).
        var verdict = Decide(sessions: [new SessionObservation(At(90), TimeSpan.Zero, TimeSpan.Zero, true)]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.SessionReachedEnd, verdict.Reason);
    }

    [Fact]
    public void SessionEvidenceBeatsTheHandOffFallbackWhenItStopsShort()
    {
        var verdict = Decide(handOffs: [new HandedOff(At(0))], sessions: [Session(30, 20)]);

        Assert.False(verdict.IsWatched);
        Assert.Equal(WatchedReason.SessionStoppedShort, verdict.Reason);
    }

    [Fact]
    public void TheClosingCreditsCountAsTheEnd()
    {
        // Within the last few minutes of a 44-minute episode is the credits; ten minutes short is not.
        var creditsAt = Math.Ceiling(Episode.TotalMinutes * Watched.EndFraction);

        Assert.True(Decide(sessions: [Session(50, creditsAt)]).IsWatched);
        Assert.False(Decide(sessions: [Session(50, Episode.TotalMinutes - 10)]).IsWatched);
    }

    [Fact]
    public void AnUnknownDurationNeverCountsAsReachingTheEnd()
    {
        var verdict = Decide(sessions: [new SessionObservation(At(10), TimeSpan.FromMinutes(80), TimeSpan.Zero, false)]);

        Assert.False(verdict.IsWatched);
    }

    [Fact]
    public void OnceTheEndWasReachedAShortRewatchDoesNotUnwatchIt()
    {
        var verdict = Decide(sessions: [Session(50, 44), Session(200, 3)]);

        Assert.True(verdict.IsWatched);
    }

    [Fact]
    public void MarkingUnwatchedOverridesEverythingBeforeIt()
    {
        var verdict = Decide(
            handOffs: [new HandedOff(At(0))],
            sessions: [Session(50, 44)],
            marks: [new ManualMark(At(60), false)]);

        Assert.False(verdict.IsWatched);
        Assert.Equal(WatchedReason.MarkedUnwatched, verdict.Reason);
    }

    [Fact]
    public void MarkingWatchedOverridesAShortSession()
    {
        var verdict = Decide(sessions: [Session(30, 10)], marks: [new ManualMark(At(40), true)]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.MarkedWatched, verdict.Reason);
    }

    [Fact]
    public void TheLatestMarkIsTheOneThatCounts()
    {
        var verdict = Decide(marks: [new ManualMark(At(70), false), new ManualMark(At(10), true)]);

        Assert.False(verdict.IsWatched);
    }

    [Fact]
    public void AMarkAndEvidenceAtTheSameMomentLeaveTheMarkInCharge()
    {
        var verdict = Decide(sessions: [Session(30, 44)], marks: [new ManualMark(At(30), false)]);

        Assert.Equal(WatchedReason.MarkedUnwatched, verdict.Reason);
    }

    [Fact]
    public void WatchingAgainAfterMarkingUnwatchedIsLearnedAgain()
    {
        // Marked unwatched to watch it again, then watched it again to the end: the newer evidence
        // speaks. Without this a title once marked unwatched could never be learned as watched.
        var verdict = Decide(
            sessions: [Session(30, 44), Session(300, 44)],
            marks: [new ManualMark(At(100), false)]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.SessionReachedEnd, verdict.Reason);
    }

    [Fact]
    public void AHandOffAfterMarkingUnwatchedFallsBackToWatched()
    {
        var verdict = Decide(handOffs: [new HandedOff(At(200))], marks: [new ManualMark(At(100), false)]);

        Assert.True(verdict.IsWatched);
        Assert.Equal(WatchedReason.HandedOff, verdict.Reason);
    }

    [Fact]
    public void ASessionOnlyBeforeTheMarkDoesNotOutvoteAHandOffAfterIt()
    {
        var verdict = Decide(
            handOffs: [new HandedOff(At(200))],
            sessions: [Session(30, 5)],
            marks: [new ManualMark(At(100), false)]);

        Assert.Equal(WatchedReason.HandedOff, verdict.Reason);
    }

    [Fact]
    public void ImpossibleObservationsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionObservation(At(0), TimeSpan.FromMinutes(-1), Episode, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionObservation(At(0), TimeSpan.Zero, TimeSpan.FromMinutes(-1), false));
    }

    [Fact]
    public void TheEndFractionLeavesRoomForCreditsButNotForHalfAnEpisode()
    {
        Assert.InRange(Watched.EndFraction, 0.85, 0.99);
    }
}
