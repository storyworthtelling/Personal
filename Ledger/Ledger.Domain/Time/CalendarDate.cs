using System.Globalization;

namespace Index.Domain.Time;

/// <summary>
/// Date-only values: a next payment due date, a transaction's posted date. Plaid sends these as
/// "YYYY-MM-DD" with no time and no zone, and Index stores them as that calendar day at UTC midnight
/// (<c>PlaidClient.OptionalDate</c>; a hand-entered date goes through <see cref="ToStored"/>). Read back
/// from SQLite they carry offset zero (<c>LedgerDbContext</c>'s ticks converter rebuilds them that way).
///
/// <para>The trap is that a <see cref="DateTimeOffset"/> is an INSTANT, and rendering one through
/// <c>.LocalDateTime</c> shifts it into the phone's zone: UTC midnight on Oct 3 is Oct 2 at 5 pm in a
/// UTC-7 zone, so the card said "Due Oct 2" for an Oct 3 bill. A date-only value has no zone to shift
/// into, so it is never converted — this class is the one place that reads the calendar day back out.
/// Genuine instants (last refreshed, a balance's "as of") still use local time, which is correct for
/// them.</para>
/// </summary>
public static class CalendarDate
{
    /// <summary>The calendar day a stored date-only value names. Reads the UTC day whatever offset the
    /// value happens to be presented in, so a value that has already been through
    /// <c>ToLocalTime()</c>/<c>ToOffset</c> still names the same day.</summary>
    public static DateOnly Of(DateTimeOffset stored) => DateOnly.FromDateTime(stored.UtcDateTime);

    /// <summary>The stored form of a calendar day: that day at UTC midnight, the shape Plaid dates take.</summary>
    public static DateTimeOffset ToStored(DateOnly day) =>
        new(day.Year, day.Month, day.Day, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The calendar day an INSTANT falls on for the person holding the phone (or in <paramref name="zone"/>,
    /// for a test). The counterpart of <see cref="Of"/>: that one reads a stored date and must not convert; this
    /// one reads a moment and must. A receipt that arrives at 02:00 UTC on Sep 27 is still Sep 26 evening on a
    /// US phone.</summary>
    public static DateOnly PhoneDay(DateTimeOffset instant, TimeZoneInfo? zone = null) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone ?? TimeZoneInfo.Local).DateTime);

    /// <summary>"Oct 3" — the day, in the form Index prints it.</summary>
    public static string Short(DateTimeOffset stored) =>
        Of(stored).ToString("MMM d", CultureInfo.InvariantCulture);

    /// <summary>What a MAUI <c>DatePicker</c> is handed: the stored day as an unspecified-kind date.</summary>
    public static DateTime ForPicker(DateTimeOffset stored) => Of(stored).ToDateTime(TimeOnly.MinValue);

    /// <summary>The day a <c>DatePicker</c> returned. The picker's value is a local calendar date with no
    /// time that matters, so only its date part is taken.</summary>
    public static DateOnly FromPicker(DateTime picked) => DateOnly.FromDateTime(picked);
}
