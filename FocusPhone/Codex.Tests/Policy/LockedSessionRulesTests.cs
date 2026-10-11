using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// When a locked hand-off ends (D288, D297): Home, a long pause, the title ending or changing, the
/// six-hour cap, or a restart that finds a lock nobody can still be using.
/// </summary>
public class LockedSessionRulesTests
{
    private const string Netflix = "com.netflix.mediaclient";
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 20, 0, 0, TimeSpan.Zero);

    private static LockedSessionRecord Record(string? title = null, DateTimeOffset? pausedSince = null) =>
        new(Netflix, Start, 50_000, [], 0, false, title, 0, 0, pausedSince);

    private static PlaybackSessionReading Reading(
        PlaybackSessionState state, string? title = "Harbor Lights", long position = 60_000, long duration = 5_400_000,
        string package = Netflix) => new(package, title, position, duration, state);

    private static readonly DateTimeOffset Settled = Start + LockedSessionRules.SettleWindow + TimeSpan.FromSeconds(1);

    [Fact]
    public void HomePressedEndsTheSession()
    {
        Assert.Equal(LockedReleaseReason.HomePressed, LockedSessionRules.OnHome(Record(), Start.AddMinutes(10)));
    }

    [Fact]
    public void AHomeResumeRightAfterTheStartIsTheStartNotAPress()
    {
        Assert.Null(LockedSessionRules.OnHome(Record(), Start + LockedSessionRules.HomeGrace - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(LockedReleaseReason.HomePressed, LockedSessionRules.OnHome(Record(), Start + LockedSessionRules.HomeGrace));
    }

    [Fact]
    public void PlayingKeepsTheSessionAndRemembersTheTitleAndPlace()
    {
        var verdict = LockedSessionRules.OnReading(Record(), Reading(PlaybackSessionState.Playing, position: 90_000), Settled);

        Assert.Null(verdict.Release);
        Assert.Equal("Harbor Lights", verdict.Record.Title);
        Assert.Equal(90_000, verdict.Record.LastPositionMs);
        Assert.Equal(5_400_000, verdict.Record.LastDurationMs);
        Assert.Null(verdict.Record.PausedSince);
    }

    [Fact]
    public void ALongPauseEndsItAndAShortOneDoesNot()
    {
        var paused = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        Assert.Null(paused.Release);
        Assert.Equal(Settled, paused.Record.PausedSince);

        Assert.Null(LockedSessionRules.OnTick(paused.Record, Settled + LockedSessionRules.LongPause - TimeSpan.FromSeconds(1)));
        Assert.Equal(LockedReleaseReason.LongPause, LockedSessionRules.OnTick(paused.Record, Settled + LockedSessionRules.LongPause));
    }

    [Fact]
    public void ResumingClearsThePause()
    {
        var paused = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        var resumed = LockedSessionRules.OnReading(paused.Record, Reading(PlaybackSessionState.Playing), Settled.AddMinutes(5));

        Assert.Null(resumed.Record.PausedSince);
        Assert.Null(LockedSessionRules.OnTick(resumed.Record, Settled.AddMinutes(45)));
    }

    [Fact]
    public void AnotherPauseReadingDoesNotRestartTheClock()
    {
        var first = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        var second = LockedSessionRules.OnReading(first.Record, Reading(PlaybackSessionState.Paused, position: 61_000), Settled.AddMinutes(20));

        Assert.Equal(Settled, second.Record.PausedSince);
    }

    [Fact]
    public void AReadingThatArrivesAfterALongPauseEndsItToo()
    {
        var paused = Record("Harbor Lights", Settled);

        var verdict = LockedSessionRules.OnReading(paused, Reading(PlaybackSessionState.Paused), Settled + LockedSessionRules.LongPause);

        Assert.Equal(LockedReleaseReason.LongPause, verdict.Release);
    }

    [Fact]
    public void TheAppClosingItsSessionIsTheTitleEndingAndIsRelayedByTheWatcherNotByUs()
    {
        var verdict = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Stopped), Settled);

        Assert.Equal(LockedReleaseReason.TitleEnded, verdict.Release);
        Assert.Null(verdict.Closing);
    }

    [Fact]
    public void ADifferentTitleEndsItAndClosesTheOldOneAtItsLastPlace()
    {
        var known = LockedSessionRules.OnReading(
            Record("Harbor Lights"), Reading(PlaybackSessionState.Playing, position: 5_300_000), Settled).Record;

        var verdict = LockedSessionRules.OnReading(
            known, Reading(PlaybackSessionState.Playing, "Harbor Lights, Part Two", position: 1_000), Settled.AddMinutes(2));

        Assert.Equal(LockedReleaseReason.TitleChanged, verdict.Release);
        Assert.NotNull(verdict.Closing);
        Assert.Equal(PlaybackSessionState.Stopped, verdict.Closing!.State);
        Assert.Equal("Harbor Lights", verdict.Closing.Title);
        Assert.Equal(5_300_000, verdict.Closing.PositionMs);
        Assert.Equal(5_400_000, verdict.Closing.DurationMs);
        Assert.Equal(Netflix, verdict.Closing.Package);
    }

    [Fact]
    public void TheSameTitleInDifferentCaseOrSpacingIsNotAChange()
    {
        var known = Record("Harbor Lights");

        Assert.Null(LockedSessionRules.OnReading(known, Reading(PlaybackSessionState.Playing, "  harbor lights "), Settled).Release);
    }

    [Fact]
    public void AReadingWithNoTitleNeverChangesIt()
    {
        var verdict = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Playing, title: null), Settled);

        Assert.Null(verdict.Release);
        Assert.Equal("Harbor Lights", verdict.Record.Title);
    }

    [Fact]
    public void WhileTheAppSettlesAStaleCloseOrAnOldTitleDoesNotEndTheSession()
    {
        var early = Start.AddSeconds(2);

        var staleClose = LockedSessionRules.OnReading(Record(), Reading(PlaybackSessionState.Stopped), early);
        Assert.Null(staleClose.Release);

        var oldTitle = LockedSessionRules.OnReading(Record("Last Night's Show"), Reading(PlaybackSessionState.Playing, "Harbor Lights"), early);
        Assert.Null(oldTitle.Release);
        Assert.Equal("Harbor Lights", oldTitle.Record.Title);
    }

    [Fact]
    public void AnotherAppsReadingIsNoneOfOurBusiness()
    {
        var verdict = LockedSessionRules.OnReading(
            Record("Harbor Lights"), Reading(PlaybackSessionState.Stopped, package: "com.hulu.plus"), Settled);

        Assert.Null(verdict.Release);
        Assert.Equal(Record("Harbor Lights"), verdict.Record);
    }

    [Fact]
    public void SixHoursIsTheCapOnAnyEvent()
    {
        var late = Start + LockedSessionRules.MaxDuration;

        Assert.Equal(TimeSpan.FromHours(6), LockedSessionRules.MaxDuration);
        Assert.Equal(LockedReleaseReason.MaxDuration, LockedSessionRules.OnTick(Record("Harbor Lights"), late));
        Assert.Null(LockedSessionRules.OnTick(Record("Harbor Lights"), late - TimeSpan.FromSeconds(1)));
        Assert.Equal(LockedReleaseReason.MaxDuration,
            LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Playing), late).Release);
        Assert.Equal(LockedReleaseReason.MaxDuration, LockedSessionRules.OnHome(Record(), late));
    }

    [Fact]
    public void ARestartAfterARebootFindsAStaleLock()
    {
        // The elapsed-realtime clock restarts at boot, so a smaller reading than the record's means a reboot.
        Assert.Equal(LockedReleaseReason.Stale, LockedSessionRules.OnProcessStart(Record(), Start.AddMinutes(30), 49_999));
    }

    [Fact]
    public void ARestartPastTheCapOrAfterAClockJumpBackIsStale()
    {
        Assert.Equal(LockedReleaseReason.Stale,
            LockedSessionRules.OnProcessStart(Record(), Start + LockedSessionRules.MaxDuration, 60_000));
        Assert.Equal(LockedReleaseReason.Stale,
            LockedSessionRules.OnProcessStart(Record(), Start.AddHours(-2), 60_000));
    }

    [Fact]
    public void ARestartMidFilmWithoutARebootLeavesTheSessionRunning()
    {
        Assert.Null(LockedSessionRules.OnProcessStart(Record(), Start.AddMinutes(40), 2_450_000));
    }

    [Fact]
    public void TheTickIntervalIsShorterThanTheLongPause()
    {
        Assert.True(LockedSessionRules.TickInterval < LockedSessionRules.LongPause);
    }
}
