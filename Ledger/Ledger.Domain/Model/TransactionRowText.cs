using System.Globalization;
using Index.Domain.Time;

namespace Index.Domain.Model;

/// <summary>
/// Mockup 3 (remediation 3.3): the second line of each transaction on an account's post-it — "Yesterday ·
/// Groceries", "Tuesday · Fuel", "Sep 21 · Household". A relative day while it is still this week, the date
/// after that, then the OWNER's category name (the one Budget stored for the transaction), and "pending"
/// while the bank still calls it that.
///
/// <para>A transaction's posted date is a date-only value (stored at UTC midnight), so it is read through
/// <see cref="CalendarDate"/> and never shifted into the phone's zone. "Today" is the phone's local day,
/// passed in so the rule is testable. The relative wording is the suite's own (<see cref="RelativeTime"/>);
/// this only adds "Today", a leading capital (a day starts a line), and the date-only reading.</para>
///
/// <para>A transaction with no stored category shows none — an uncategorized transaction is a thing the
/// owner can see and settle in Budget, and a guess here would file money under the wrong name with
/// nothing to show it.</para>
/// </summary>
public static class TransactionRowText
{
    /// <summary>"Today", "Yesterday", a weekday inside the week, otherwise "Sep 21" (with the year from another year).</summary>
    public static string Day(DateOnly day, DateOnly today)
    {
        if (day == today)
        {
            return "Today";
        }

        if (day > today)
        {
            // A date ahead of the phone's day (a pending item the bank has dated forward): the date itself.
            return day.ToString("MMM d", CultureInfo.InvariantCulture);
        }

        // Midnight to midnight, so RelativeTime sees whole days: 1 -> "yesterday", 2..6 -> the weekday,
        // 7 or more -> the date.
        var phrase = RelativeTime.Format(day.ToDateTime(TimeOnly.MinValue), today.ToDateTime(TimeOnly.MinValue));
        return char.ToUpper(phrase[0], CultureInfo.InvariantCulture) + phrase[1..];
    }

    public static string WhenAndCategory(DateTimeOffset storedDate, DateOnly today, bool pending, string? categoryName)
    {
        var text = Day(CalendarDate.Of(storedDate), today);
        if (!string.IsNullOrWhiteSpace(categoryName))
        {
            text += " · " + categoryName.Trim();
        }

        return pending ? text + " · pending" : text;
    }
}
