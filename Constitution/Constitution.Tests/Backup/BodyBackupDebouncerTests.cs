using Constitution.Infrastructure.Backup;

namespace Constitution.Tests.Backup;

/// <summary>
/// C-7.4.1 — the backup is written debounced. The debouncer decides and holds no clock: time is
/// passed in, so every rule below is a plain sum on a date.
/// </summary>
public class BodyBackupDebouncerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 17, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void TheWindowsAreNamedAndSensible()
    {
        Assert.InRange(BodyBackupDebouncer.QuietWindow, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));
        Assert.True(BodyBackupDebouncer.LongestWait > BodyBackupDebouncer.QuietWindow);
        Assert.InRange(BodyBackupDebouncer.LongestWait, TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
    }

    [Fact]
    public void NothingPendingIsNeverDue()
    {
        var debouncer = new BodyBackupDebouncer();

        Assert.False(debouncer.HasPending);
        Assert.Null(debouncer.DueAt);
        Assert.False(debouncer.IsDue(Start.AddYears(1)));
        Assert.False(debouncer.TakeIfDue(Start.AddYears(1)));
    }

    [Fact]
    public void AChangeIsDueOnlyOnceTheQuietWindowHasPassed()
    {
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Start);

        Assert.True(debouncer.HasPending);
        Assert.Equal(Start + BodyBackupDebouncer.QuietWindow, debouncer.DueAt);
        Assert.False(debouncer.IsDue(Start));
        Assert.False(debouncer.IsDue(Start + BodyBackupDebouncer.QuietWindow - TimeSpan.FromSeconds(1)));
        Assert.True(debouncer.IsDue(Start + BodyBackupDebouncer.QuietWindow));
    }

    [Fact]
    public void FurtherChangesPushTheWindowOut()
    {
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Start);
        var later = Start + BodyBackupDebouncer.QuietWindow - TimeSpan.FromSeconds(1);
        debouncer.Changed(later);

        Assert.False(debouncer.IsDue(Start + BodyBackupDebouncer.QuietWindow));
        Assert.True(debouncer.IsDue(later + BodyBackupDebouncer.QuietWindow));
        Assert.Equal(later + BodyBackupDebouncer.QuietWindow, debouncer.DueAt);
    }

    [Fact]
    public void ManyChangesInAWindowAreOneTake()
    {
        var debouncer = new BodyBackupDebouncer();
        for (var second = 0; second < 30; second++)
        {
            debouncer.Changed(Start.AddSeconds(second));
        }

        var due = Start.AddSeconds(29) + BodyBackupDebouncer.QuietWindow;

        Assert.True(debouncer.TakeIfDue(due));
        Assert.False(debouncer.TakeIfDue(due));
        Assert.False(debouncer.HasPending);
    }

    [Fact]
    public void ANeverEndingStreamIsStillWrittenEventually()
    {
        var debouncer = new BodyBackupDebouncer();
        var now = Start;
        debouncer.Changed(now);

        // A change every ten seconds would keep pushing the quiet window out for ever.
        while (now - Start < BodyBackupDebouncer.LongestWait)
        {
            Assert.False(debouncer.IsDue(now));
            now = now.AddSeconds(10);
            debouncer.Changed(now);
        }

        Assert.True(debouncer.IsDue(Start + BodyBackupDebouncer.LongestWait));
        Assert.Equal(Start + BodyBackupDebouncer.LongestWait, debouncer.DueAt);
    }

    [Fact]
    public void AChangeAfterATakeStartsAFreshWindow()
    {
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Start);
        Assert.True(debouncer.TakeIfDue(Start + BodyBackupDebouncer.QuietWindow));

        var second = Start.AddHours(1);
        debouncer.Changed(second);

        Assert.Equal(second + BodyBackupDebouncer.QuietWindow, debouncer.DueAt);
        Assert.False(debouncer.IsDue(second));
    }

    [Fact]
    public void TakingNowFlushesOnDemand()
    {
        var debouncer = new BodyBackupDebouncer();

        Assert.False(debouncer.TakeNow());

        debouncer.Changed(Start);
        Assert.True(debouncer.TakeNow());
        Assert.False(debouncer.HasPending);
        Assert.False(debouncer.TakeNow());
    }

    [Fact]
    public void AClockThatWentBackwardsNeverMakesAChangeDueEarly()
    {
        var debouncer = new BodyBackupDebouncer();
        debouncer.Changed(Start);

        Assert.False(debouncer.IsDue(Start.AddHours(-3)));
    }

    [Fact]
    public async Task ChangesFromManyThreadsAreStillOneTake()
    {
        var debouncer = new BodyBackupDebouncer();

        await Task.WhenAll(Enumerable.Range(0, 200).Select(index =>
            Task.Run(() => debouncer.Changed(Start.AddMilliseconds(index)))));

        Assert.True(debouncer.HasPending);
        Assert.True(debouncer.TakeNow());
        Assert.False(debouncer.TakeNow());
    }
}
