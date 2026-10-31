using Constitution.Domain.Calendar;

namespace Constitution.Tests.Calendar;

/// <summary>
/// The invented household every calendar test stands on: Sam and Lee are the adults, Robin and
/// Juniper the children, and every address is at example.com. Nothing here is a real person, place
/// or time of anyone's day.
///
/// <para><b>Expected instants are written out, not computed.</b> A test that built its expectation
/// with the code under test (<c>ZoneClock</c>) would agree with it whatever it did, so the helpers
/// below carry a fixed UTC offset: <see cref="Edt"/> is UTC-4 (October and the second half of
/// March), <see cref="Est"/> is UTC-5 (the winter). The zone itself is a custom zone built from
/// the US-Eastern rules, so the suite does not depend on a time-zone database being installed.</para>
/// </summary>
internal static class InventedFamily
{
    public const string Sam = "Sam";
    public const string Lee = "Lee";
    public const string Robin = "Robin";
    public const string Juniper = "Juniper";

    public const string SamAddress = "sam@example.com";
    public const string LeeAddress = "lee@example.com";

    /// <summary>
    /// The shape Foreword saves its work schedule in, written out as the file (a round trip through
    /// this repo's own code would pass with the shape changed at both ends). Sam works 8 to 4, Lee 8
    /// to 3:30, weekdays, with Foreword's own off-day words.
    /// </summary>
    public const string ForewordSchedule =
        "{\"Shifts\":[" +
        "{\"Person\":\"Sam\",\"Start\":\"08:00\",\"End\":\"16:00\",\"Title\":\"Work\",\"Enabled\":true}," +
        "{\"Person\":\"Lee\",\"Start\":\"08:00\",\"End\":\"15:30\",\"Title\":\"Work\",\"Enabled\":true}]," +
        "\"OffKeywords\":[\"Off\",\"PTO\",\"Vacation\"]}";

    /// <summary>A Monday on daylight time, a week before the clocks go back.</summary>
    public static readonly DateOnly Monday = new(2026, 10, 26);

    /// <summary>The Sunday the day before <see cref="Monday"/>: the first day of the planning week that contains it, on daylight time throughout.</summary>
    public static readonly DateOnly Sunday = new(2026, 10, 25);

    /// <summary>The US-Eastern rules as a custom zone: forward the second Sunday of March at 2 am, back the first of November.</summary>
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "Invented Eastern",
        TimeSpan.FromHours(-5),
        "Invented Eastern",
        "Invented Eastern Standard",
        "Invented Eastern Daylight",
        [
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                DateTime.MinValue.Date,
                DateTime.MaxValue.Date,
                TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 3, 2, DayOfWeek.Sunday),
                TimeZoneInfo.TransitionTime.CreateFloatingDateRule(new DateTime(1, 1, 1, 2, 0, 0), 11, 1, DayOfWeek.Sunday)),
        ]);

    /// <summary>A 2026 wall-clock time on daylight saving time (UTC-4).</summary>
    public static DateTimeOffset Edt(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(-4));

    /// <summary>A 2026 wall-clock time on standard time (UTC-5).</summary>
    public static DateTimeOffset Est(int month, int day, int hour, int minute = 0) =>
        new(2026, month, day, hour, minute, 0, TimeSpan.FromHours(-5));

    /// <summary>A timed entry on Monday 26 October 2026, hours as on the clock.</summary>
    public static CalendarEntry OnMonday(string title, int fromHour, int toHour, Availability availability = Availability.Busy, IReadOnlyList<string>? guests = null) =>
        CalendarEntry.Timed(title, Edt(10, 26, fromHour), Edt(10, 26, toHour), availability, guests);

    public static Adult SamAdult(bool onPickup = true) => new(Sam, onPickup, [SamAddress]);

    public static Adult LeeAdult(bool onPickup = true) => new(Lee, onPickup, [LeeAddress]);

    /// <summary>
    /// The rules with clean numbers: dinner 6 to 7, a 45-minute bedtime routine ending at 8, and a
    /// WFH lunch from noon to one. Each can be replaced; the work schedule is Foreword's file.
    /// </summary>
    public static PlanningRules Rules(
        WorkSchedule? work = null,
        FamilyDinner? dinner = null,
        BedtimeRoutine? bedtime = null,
        WorkFromHomeLunch? lunch = null,
        bool samOnPickup = true,
        bool leeOnPickup = true) =>
        new(
            Zone,
            [SamAdult(samOnPickup), LeeAdult(leeOnPickup)],
            work ?? WorkSchedule.Read(ForewordSchedule),
            dinner ?? new FamilyDinner(new TimeOnly(18, 0), TimeSpan.FromMinutes(60)),
            bedtime ?? new BedtimeRoutine(new TimeOnly(20, 0), TimeSpan.FromMinutes(45)),
            lunch ?? new WorkFromHomeLunch(new TimeOnly(12, 0), TimeSpan.FromMinutes(60), ["WFH", "Work from home"]));

    /// <summary>The four calendars as the head would hand them over, one list each.</summary>
    public static HouseholdCalendarSnapshot Calendars(
        IEnumerable<CalendarEntry>? sam = null,
        IEnumerable<CalendarEntry>? lee = null,
        IEnumerable<CalendarEntry>? family = null,
        IEnumerable<CalendarEntry>? children = null) =>
        new(
            new Dictionary<string, CalendarRead>(StringComparer.OrdinalIgnoreCase)
            {
                [Sam] = CalendarRead.Of(sam ?? []),
                [Lee] = CalendarRead.Of(lee ?? []),
            },
            CalendarRead.Of(family ?? []),
            CalendarRead.Of(children ?? []));

    /// <summary>The same, with some of the calendars unreadable.</summary>
    public static HouseholdCalendarSnapshot CalendarsWith(
        CalendarRead? sam = null,
        CalendarRead? lee = null,
        CalendarRead? family = null,
        CalendarRead? children = null) =>
        new(
            new Dictionary<string, CalendarRead>(StringComparer.OrdinalIgnoreCase)
            {
                [Sam] = sam ?? CalendarRead.Of([]),
                [Lee] = lee ?? CalendarRead.Of([]),
            },
            family ?? CalendarRead.Of([]),
            children ?? CalendarRead.Of([]));
}
