using Pulp.Domain.Calendar;
using Pulp.Domain.Core;

namespace Pulp.Tests.Calendar;

/// <summary>The nightly write runs the plan and nothing but the plan.</summary>
public class CalendarWriterTests
{
    private static readonly DateOnly Today = new(2026, 10, 9);
    private static readonly ViewerId Sam = new("sam");
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;

    private static CalendarItem Item(string key, DateOnly date) =>
        new(key, CalendarItemKind.NewEpisode, CalendarWhen.AllDay(date), "Harbor Lights", "S1 E4", null, null, null, [Sam], []);

    [Fact]
    public async Task ItCreatesMovesAndDeletesOnlyWhatThePlanSays()
    {
        var calendar = new FakePulpCalendar();
        calendar.Held.Add(new ExistingEvent("e-move", "episode:a:1:1", "x", null, CalendarWhen.AllDay(Today)));
        calendar.Held.Add(new ExistingEvent("e-gone", "episode:b:1:1", "x", null, CalendarWhen.AllDay(Today.AddDays(2))));
        calendar.Held.Add(new ExistingEvent("e-hand", null, "Dentist", null, CalendarWhen.AllDay(Today.AddDays(2))));

        var result = await new CalendarWriter(calendar).SyncAsync(
            "cal-1", [Item("episode:a:1:1", Today.AddDays(4)), Item("episode:c:1:1", Today)], _ => "Sam", Today, Zone, CancellationToken.None);

        Assert.Equal(["delete:e-gone", "patch:e-move", "insert:episode:c:1:1"], calendar.Calls);
        Assert.DoesNotContain(calendar.Calls, call => call.Contains("e-hand"));
        Assert.Equal(new CalendarSyncResult(1, 1, 1, 0), result);
    }

    [Fact]
    public async Task ACancelledTokenStopsTheWriteAndPropagates()
    {
        var calendar = new FakePulpCalendar();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CalendarWriter(calendar).SyncAsync("cal-1", [Item("episode:c:1:1", Today)], _ => "Sam", Today, Zone, source.Token));
        Assert.Empty(calendar.Calls);
    }

    private static FakePulpCalendar HoldingASpoilerAndAMove()
    {
        var calendar = new FakePulpCalendar();
        calendar.Held.Add(new ExistingEvent("e-spoiler", "game:old", "x", null, CalendarWhen.AllDay(Today.AddDays(2))));
        calendar.Held.Add(new ExistingEvent("e-move", "episode:a:1:1", "x", null, CalendarWhen.AllDay(Today)));
        return calendar;
    }

    private static CalendarItem[] MoveAndCreate() => [Item("episode:a:1:1", Today.AddDays(4)), Item("episode:c:1:1", Today)];

    [Fact]
    public async Task AFailingCreateDoesNotKeepAHeldGameOffTheSharedCalendar()
    {
        // Bug kept out: creates ran first and the first failure threw, so the delete that removes a spoiler never ran.
        var calendar = HoldingASpoilerAndAMove();
        calendar.Failing.Add("insert:episode:c:1:1");

        var failure = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new CalendarWriter(calendar).SyncAsync("cal-1", MoveAndCreate(), _ => "Sam", Today, Zone, CancellationToken.None));

        Assert.Contains("delete:e-spoiler", calendar.Calls);
        Assert.Contains("patch:e-move", calendar.Calls);
        Assert.Single(failure.InnerExceptions);
        Assert.Equal(new CalendarSyncResult(0, 1, 1, 0), failure.Done);
    }

    [Fact]
    public async Task AFailingDeleteDoesNotStopTheMovesAndCreatesAfterIt()
    {
        var calendar = HoldingASpoilerAndAMove();
        calendar.Failing.Add("delete:e-spoiler");

        var failure = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new CalendarWriter(calendar).SyncAsync("cal-1", MoveAndCreate(), _ => "Sam", Today, Zone, CancellationToken.None));

        Assert.Equal(["delete:e-spoiler", "patch:e-move", "insert:episode:c:1:1"], calendar.Calls);
        Assert.Equal(new CalendarSyncResult(1, 1, 0, 0), failure.Done);
    }

    [Fact]
    public async Task EveryFailureIsCollectedNotJustTheFirst()
    {
        var calendar = HoldingASpoilerAndAMove();
        calendar.Failing.UnionWith(["delete:e-spoiler", "patch:e-move", "insert:episode:c:1:1"]);

        var failure = await Assert.ThrowsAsync<CalendarSyncException>(() =>
            new CalendarWriter(calendar).SyncAsync("cal-1", MoveAndCreate(), _ => "Sam", Today, Zone, CancellationToken.None));

        Assert.Equal(3, failure.InnerExceptions.Count);
    }

    [Fact]
    public async Task ACancellationMidWritePropagatesAtOnceInsteadOfBeingCollected()
    {
        var calendar = HoldingASpoilerAndAMove();
        using var source = new CancellationTokenSource();
        calendar.OnCall = () => source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CalendarWriter(calendar).SyncAsync("cal-1", MoveAndCreate(), _ => "Sam", Today, Zone, source.Token));

        Assert.Equal(["delete:e-spoiler"], calendar.Calls);
    }
}
