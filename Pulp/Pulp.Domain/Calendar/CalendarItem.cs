using Pulp.Domain.Core;
using Pulp.Domain.Sports;

namespace Pulp.Domain.Calendar;

/// <summary>What a calendar line is (D146). No leaving-soon kind: those dates stay on the row.</summary>
public enum CalendarItemKind
{
    NewEpisode,
    Release,
    Game,
    Plan,
}

/// <summary>
/// When an item happens, in the household's own zone: a day, and a time only where one is known
/// (D148). No time means all-day.
/// </summary>
public readonly record struct CalendarWhen(DateOnly Date, TimeOnly? Time)
{
    public bool IsAllDay => Time is null;

    public static CalendarWhen AllDay(DateOnly date) => new(date, null);

    public static CalendarWhen At(DateTimeOffset instant, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        return new CalendarWhen(DateOnly.FromDateTime(local.DateTime), TimeOnly.FromDateTime(local.DateTime));
    }
}

/// <summary>
/// One line on the Calendar page and one event on the household Google calendar.
/// <paramref name="Key"/> names the thing, never its date, so a moved date or a rescheduled game
/// moves the existing event instead of adding a second (D149). <paramref name="Whose"/> is who it
/// belongs to (D147); <paramref name="Playlists"/> names the playlists a release is on.
/// </summary>
public sealed record CalendarItem(
    string Key,
    CalendarItemKind Kind,
    CalendarWhen When,
    string Name,
    string? Detail,
    ServiceId? Service,
    TitleId? Title,
    LeagueId? League,
    IReadOnlyList<ViewerId> Whose,
    IReadOnlyList<string> Playlists);

/// <summary>One day's group on the Calendar page.</summary>
public sealed record CalendarDay(DateOnly Date, IReadOnlyList<CalendarItem> Items);
