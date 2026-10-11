using Codex.Domain.Apps;
using Codex.Domain.Policy;
using Xunit;

namespace Codex.Tests.Policy;

/// <summary>
/// When a locked hand-off ends (D288, D297, D301): Home, a long pause, the title ending or changing,
/// the six-hour cap, an app that never started, or a restart that finds a lock nobody can still be
/// using. Every running rule reads the monotonic elapsed-realtime clock, so a wall-clock change cannot
/// shorten, lengthen or end a session; the wall clock is read only at process start, to notice that
/// it moved.
/// </summary>
public class LockedSessionRulesTests
{
    private const string Netflix = "com.netflix.mediaclient";
    private const long StartMs = 50_000;
    private static readonly DateTimeOffset Start = new(2026, 3, 4, 20, 0, 0, TimeSpan.Zero);

    private static long At(TimeSpan since) => StartMs + (long)since.TotalMilliseconds;

    private static LockedSessionRecord Record(
        string? title = null, long? pausedSince = null, bool confirmed = true, bool advanced = false, int bootCount = -1) =>
        new(Netflix, Start, StartMs, [], 0, false, title, 0, 0, pausedSince, confirmed, advanced, bootCount);

    private static PlaybackSessionReading Reading(
        PlaybackSessionState state, string? title = "Harbor Lights", long position = 60_000, long duration = 5_400_000,
        string package = Netflix) => new(package, title, position, duration, state);

    private static readonly long Settled = At(LockedSessionRules.SettleWindow + TimeSpan.FromSeconds(1));

    [Fact]
    public void HomePressedEndsTheSession()
    {
        Assert.Equal(LockedReleaseReason.HomePressed, LockedSessionRules.OnHome(Record(), At(TimeSpan.FromMinutes(10))));
    }

    [Fact]
    public void AHomeResumeRightAfterTheStartIsTheStartNotAPress()
    {
        Assert.Null(LockedSessionRules.OnHome(Record(), At(LockedSessionRules.HomeGrace) - 1));
        Assert.Equal(LockedReleaseReason.HomePressed, LockedSessionRules.OnHome(Record(), At(LockedSessionRules.HomeGrace)));
    }

    [Fact]
    public void PlayingKeepsTheSessionAndRemembersTheTitleAndPlace()
    {
        var verdict = LockedSessionRules.OnReading(Record(), Reading(PlaybackSessionState.Playing, position: 90_000), Settled);

        Assert.Null(verdict.Release);
        Assert.Equal("Harbor Lights", verdict.Record.Title);
        Assert.Equal(90_000, verdict.Record.LastPositionMs);
        Assert.Equal(5_400_000, verdict.Record.LastDurationMs);
        Assert.Null(verdict.Record.PausedSinceElapsedMs);
    }

    [Fact]
    public void ALongPauseEndsItAndAShortOneDoesNot()
    {
        var paused = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        Assert.Null(paused.Release);
        Assert.Equal(Settled, paused.Record.PausedSinceElapsedMs);

        Assert.Null(LockedSessionRules.OnTick(paused.Record, Settled + (long)LockedSessionRules.LongPause.TotalMilliseconds - 1_000));
        Assert.Equal(LockedReleaseReason.LongPause,
            LockedSessionRules.OnTick(paused.Record, Settled + (long)LockedSessionRules.LongPause.TotalMilliseconds));
    }

    [Fact]
    public void ResumingClearsThePause()
    {
        var paused = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        var resumed = LockedSessionRules.OnReading(paused.Record, Reading(PlaybackSessionState.Playing), Settled + 300_000);

        Assert.Null(resumed.Record.PausedSinceElapsedMs);
        Assert.Null(LockedSessionRules.OnTick(resumed.Record, Settled + 45 * 60_000));
    }

    [Fact]
    public void AnotherPauseReadingDoesNotRestartTheClock()
    {
        var first = LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Paused), Settled);
        var second = LockedSessionRules.OnReading(first.Record, Reading(PlaybackSessionState.Paused, position: 61_000), Settled + 20 * 60_000);

        Assert.Equal(Settled, second.Record.PausedSinceElapsedMs);
    }

    [Fact]
    public void AReadingThatArrivesAfterALongPauseEndsItToo()
    {
        var paused = Record("Harbor Lights", Settled);

        var verdict = LockedSessionRules.OnReading(
            paused, Reading(PlaybackSessionState.Paused), Settled + (long)LockedSessionRules.LongPause.TotalMilliseconds);

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
            known, Reading(PlaybackSessionState.Playing, "Harbor Lights, Part Two", position: 1_000), Settled + 120_000);

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
        var early = At(TimeSpan.FromSeconds(2));

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
    public void SixHoursOfElapsedTimeIsTheCapOnAnyEvent()
    {
        var late = At(LockedSessionRules.MaxDuration);
        var justBefore = late - 1_000;

        Assert.Equal(TimeSpan.FromHours(6), LockedSessionRules.MaxDuration);
        Assert.Equal(LockedReleaseReason.MaxDuration, LockedSessionRules.OnTick(Record("Harbor Lights"), late));
        Assert.Null(LockedSessionRules.OnTick(Record("Harbor Lights"), justBefore));
        Assert.Equal(LockedReleaseReason.MaxDuration,
            LockedSessionRules.OnReading(Record("Harbor Lights"), Reading(PlaybackSessionState.Playing), late).Release);
        Assert.Equal(LockedReleaseReason.MaxDuration, LockedSessionRules.OnHome(Record(), late));
    }

    [Fact]
    public void AWallClockJumpDoesNotMoveTheCapOrThePause()
    {
        // The running rules take no wall time at all: nothing a user or a network time sync does to the
        // date can end a session early or let it run past its cap.
        var paused = Record("Harbor Lights", Settled);
        Assert.Null(LockedSessionRules.OnTick(paused, Settled + 60_000));
        Assert.Null(LockedSessionRules.OnTick(Record("Harbor Lights"), At(TimeSpan.FromHours(5))));
    }

    [Fact]
    public void ARestartAfterARebootFindsAStaleLock()
    {
        // The elapsed-realtime clock restarts at boot, so a smaller reading than the record's means a reboot.
        Assert.Equal(LockedReleaseReason.Stale, LockedSessionRules.OnProcessStart(Record(), Start.AddMinutes(30), 49_999));
    }

    [Fact]
    public void ARebootRightAfterAStartIsStillFoundByTheWallClockAndByTheBootCount()
    {
        // Started 20 s after boot (elapsed 20 s). Rebooted, and the new process is already 25 s into the
        // new boot: elapsed is larger than the record's, so the elapsed comparison alone says no reboot.
        var record = new LockedSessionRecord(Netflix, Start, 20_000, [], 0, false, null, 0, 0, null, true, false, 7);

        Assert.Equal(LockedReleaseReason.Stale, LockedSessionRules.OnProcessStart(record, Start.AddMinutes(10), 25_000));
        Assert.Equal(LockedReleaseReason.Stale, LockedSessionRules.OnProcessStart(record, Start.AddSeconds(5), 25_000, bootCount: 8));
        Assert.Null(LockedSessionRules.OnProcessStart(record, Start.AddSeconds(5), 25_000, bootCount: 7));
    }

    [Fact]
    public void ARestartPastTheCapOrAfterAClockJumpBackIsStale()
    {
        Assert.Equal(LockedReleaseReason.Stale,
            LockedSessionRules.OnProcessStart(Record(), Start + LockedSessionRules.MaxDuration, At(LockedSessionRules.MaxDuration)));
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

    // ---- D301: an app that never started ------------------------------------------------------

    [Fact]
    public void AnAppWhoseMediaSessionNeverAppearsIsReleasedAfterTheStartTimeout()
    {
        var unconfirmed = Record(confirmed: false);

        Assert.Equal(TimeSpan.FromSeconds(30), LockedSessionRules.StartTimeout);
        Assert.Null(LockedSessionRules.OnTick(unconfirmed, At(LockedSessionRules.StartTimeout) - 1));
        Assert.Equal(LockedReleaseReason.NeverStarted, LockedSessionRules.OnTick(unconfirmed, At(LockedSessionRules.StartTimeout)));
    }

    [Fact]
    public void AnyReadingFromTheTargetConfirmsTheStartEvenWhileItSettles()
    {
        var early = At(TimeSpan.FromSeconds(2));

        var verdict = LockedSessionRules.OnReading(Record(confirmed: false), Reading(PlaybackSessionState.Stopped), early);

        Assert.True(verdict.Record.Confirmed);
        Assert.Null(verdict.Release);
        Assert.Null(LockedSessionRules.OnTick(verdict.Record, At(TimeSpan.FromMinutes(2))));
    }

    [Fact]
    public void AConfirmedSessionIsNotTimedOut()
    {
        Assert.Null(LockedSessionRules.OnTick(Record(confirmed: true), At(TimeSpan.FromMinutes(2))));
    }

    [Fact]
    public void TheNextCheckIsSoonWhileUnconfirmedAndTheTickIntervalOnceItIs()
    {
        var soon = LockedSessionRules.NextTick(Record(confirmed: false), At(TimeSpan.FromSeconds(1)));
        Assert.True(soon > TimeSpan.Zero && soon <= LockedSessionRules.StartTimeout + TimeSpan.FromSeconds(5));

        Assert.Equal(LockedSessionRules.TickInterval, LockedSessionRules.NextTick(Record(confirmed: true), At(TimeSpan.FromMinutes(2))));
    }

    // ---- D301: a stale session's title is not a baseline worth closing --------------------------

    [Fact]
    public void ASlowStartShowingLastSessionsTitleThenTheRealOneFiresNoFalseChange()
    {
        // Within the settle window the previous session's title arrives first, then the real one.
        var stale = LockedSessionRules.OnReading(
            Record(confirmed: false), Reading(PlaybackSessionState.Playing, "Last Night's Show", position: 3_000_000), At(TimeSpan.FromSeconds(4)));
        var real = LockedSessionRules.OnReading(
            stale.Record, Reading(PlaybackSessionState.Playing, "Harbor Lights", position: 0), At(TimeSpan.FromSeconds(12)));

        Assert.Null(real.Release);
        Assert.Equal("Harbor Lights", real.Record.Title);

        // Just after the window, the title is steady: nothing to close, and nothing released.
        var later = LockedSessionRules.OnReading(
            real.Record, Reading(PlaybackSessionState.Playing, "Harbor Lights", position: 1_000), Settled);
        Assert.Null(later.Release);
        Assert.Null(later.Closing);
    }

    [Fact]
    public void ATitleChangeAfterSettlingWithNoProgressOnTheOldOneReleasesButClosesNothing()
    {
        // The baseline was taken and the position never moved past it: no evidence the old title played.
        var baseline = LockedSessionRules.OnReading(Record(), Reading(PlaybackSessionState.Playing, position: 0), Settled).Record;

        var verdict = LockedSessionRules.OnReading(
            baseline, Reading(PlaybackSessionState.Playing, "Something Else", position: 0), Settled + 60_000);

        Assert.Equal(LockedReleaseReason.TitleChanged, verdict.Release);
        Assert.Null(verdict.Closing);
    }

    [Fact]
    public void ATitleThatPlayedOnIsClosedAtItsLastPlaceWhenItChanges()
    {
        var first = LockedSessionRules.OnReading(Record(), Reading(PlaybackSessionState.Playing, position: 10_000), Settled).Record;
        var advanced = LockedSessionRules.OnReading(first, Reading(PlaybackSessionState.Playing, position: 700_000), Settled + 690_000).Record;

        Assert.True(advanced.Advanced);

        var verdict = LockedSessionRules.OnReading(
            advanced, Reading(PlaybackSessionState.Playing, "Next Episode", position: 100), Settled + 700_000);
        Assert.Equal(700_000, verdict.Closing!.PositionMs);
    }
}
