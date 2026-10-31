using Constitution.Domain.Calendar;
using Constitution.Domain.Today;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.Infrastructure.Workouts;

/// <summary>
/// Today's plan from the saved week, for the Today clipping. Pure. "Today" is the day on the owner's own clock, so the clipping changes at
/// their midnight and not at the server's or the phone's. The run's word for the day (<see cref="StoredToday"/>) is used while it is today's;
/// on any later day of the week the placed session of that day is read instead. A rest day, a day outside the week, no saved week, an
/// unreadable one, or a kind this app never wrote gives null: an absent clipping is the rest day's whole signal, and nothing is guessed.
/// Dinner is not this record's to know, so Today's top line stays neutral.
/// </summary>
public static class TodayPlanFromRecord
{
    public static TodayPlan? For(WorkoutWeekRecordRead record, string person, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(zone);
        if (record is not WorkoutWeekRecordRead.Held held)
        {
            return null;
        }

        var day = ZoneClock.DayOf(now, zone);
        var week = held.Week;
        var own = week.Sessions.FirstOrDefault(item => Same(item.Person, person) && item.Day == day);
        var said = week.Today.FirstOrDefault(item => Same(item.Person, person) && item.Day == day);

        if (said is not null)
        {
            return said.Kind is { } kind && said.Start is { } start && said.Minutes is { } minutes
                ? Plan(kind, start, minutes, said.ScoreBpm, own)
                : null;
        }

        return own is null
            ? null
            : Plan(own.Kind, TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(own.Start, zone).DateTime), own.Minutes, own.ScoreBpm, own);
    }

    private static TodayPlan? Plan(string kindWord, TimeOnly start, int minutes, int? bpm, StoredSession? session)
    {
        if (WorkoutWireNames.SessionKindOf(kindWord) is not { } kind || minutes <= 0)
        {
            return null;
        }

        var together = session is not null && Enum.TryParse<Together>(session.Together, out var parsed) ? parsed : Together.None;
        return new TodayPlan(start, KindWords.Of(kind, together), minutes, null) { ScoreBpm = bpm };
    }

    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
