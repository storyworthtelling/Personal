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

        Assert.Equal(["insert:episode:c:1:1", "patch:e-move", "delete:e-gone"], calendar.Calls.OrderBy(call => call.StartsWith("insert") ? 0 : call.StartsWith("patch") ? 1 : 2));
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
}
