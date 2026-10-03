using System.Globalization;
using Index.Domain.Time;

namespace Index.Domain.Model;

/// <summary>
/// How an account's balance date is said (mockup 15, remediation 3.3): "Updated Sep 12" for a figure the owner
/// typed, "From Post, Sep 12" for one Post read off a statement (D15).
///
/// <para>The source decides how the stored date is READ, which is why it is recorded. A statement's date is a
/// DATE (stored at UTC midnight, like a due date), so it is read through <see cref="CalendarDate"/> and never
/// shifted into the phone's zone — through <c>.LocalDateTime</c> a Sep 12 statement printed "Sep 11" on every US
/// phone. A typed or refreshed balance's as-of is an INSTANT, and the phone's own day is the right one for it.</para>
/// </summary>
public static class BalanceDisplay
{
    /// <summary>The recorded source, else what the account's own origin implies (a row from before the column existed).</summary>
    public static BalanceSource SourceOf(Account account) =>
        account.BalanceSource
        ?? (account.Source == AccountSource.Plaid ? BalanceSource.Plaid : BalanceSource.ByHand);

    /// <summary>The calendar day an as-of value names, read the way its source stores it.</summary>
    public static DateOnly Day(DateTimeOffset asOf, BalanceSource source, TimeZoneInfo? zone = null) =>
        source == BalanceSource.Post
            ? CalendarDate.Of(asOf)
            : CalendarDate.PhoneDay(asOf, zone);

    /// <summary>"Oct 3", or "Oct 3, 2025" from another year — the form the suite prints a past day in.</summary>
    public static string ShortDay(DateOnly day, DateTimeOffset now, TimeZoneInfo? zone = null) =>
        day.ToString(day.Year == CalendarDate.PhoneDay(now, zone).Year ? "MMM d" : "MMM d, yyyy", CultureInfo.InvariantCulture);

    /// <summary>The line beside "Balance" on the post-it, or null when no as-of was ever recorded.</summary>
    public static string? UpdatedLine(Account account, DateTimeOffset now, TimeZoneInfo? zone = null)
    {
        if (account.BalanceAsOfUtc is not { } asOf)
        {
            return null;
        }

        var source = SourceOf(account);
        var day = ShortDay(Day(asOf, source, zone), now, zone);
        return source == BalanceSource.Post ? $"From Post, {day}" : $"Updated {day}";
    }
}
