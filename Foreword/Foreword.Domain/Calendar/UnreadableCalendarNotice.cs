namespace Foreword.Domain.Calendar;

/// <summary>
/// The dog-ear notice for calendars that could not be read on the last morning.
///
/// <para>The worker stores the names it skipped, and the home screen shows them until a morning reads
/// every calendar again. A notice with nothing in it is withdrawn, never shown empty.</para>
/// </summary>
public static class UnreadableCalendarNotice
{
    /// <summary>The key the notice is raised under on the home screen.</summary>
    public const string Key = "unreadable-calendars";

    public static string Store(IReadOnlyList<UnreadableCalendar> unreadable) =>
        string.Join('\n', unreadable.Select(calendar => calendar.Name));

    public static IReadOnlyList<string> Names(string stored) =>
        stored.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>What the notice says, or null when there is nothing to say.</summary>
    public static string? Detail(IReadOnlyList<string> names) =>
        names.Count == 0
            ? null
            : $"Could not read {string.Join(", ", names)}. Skipped this morning; the rest of the day went out. "
              + "It will be tried again tomorrow.";
}
