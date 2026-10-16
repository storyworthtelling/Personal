using System.Globalization;
using Pulp.Domain.Core;
using Pulp.Domain.Household.Records;

namespace Pulp.Domain.Calendar;

/// <summary>
/// Plans on the calendar (D68, D146, D303): the household's plan records read as <see cref="PlannedViewing"/>,
/// and the rules for making one. A plan is the viewer's own or shared (D244); a child is never on one, so a
/// child in <see cref="HouseholdPlan.Who"/> is dropped and a plan only children are on is not read at all.
/// </summary>
public static class CalendarPlans
{
    public const int LongestText = 80;

    public const string KeyPrefix = "plan:";

    public static IReadOnlyList<PlannedViewing> ToSources(IEnumerable<HouseholdPlan> plans, IReadOnlySet<ViewerId> adults)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(adults);

        var sources = new List<PlannedViewing>();
        foreach (var plan in plans)
        {
            var who = plan.Who.Where(adults.Contains).Distinct().ToList();
            if (who.Count > 0)
            {
                sources.Add(new PlannedViewing(plan.Id, plan.Title, plan.Text, plan.Date, plan.Time, who, plan.Place));
            }
        }

        return sources;
    }

    /// <summary>The record id a plan row's key names, or null for any other kind of row.</summary>
    public static string? IdOfKey(string key) =>
        key.StartsWith(KeyPrefix, StringComparison.Ordinal) && key.Length > KeyPrefix.Length ? key[KeyPrefix.Length..] : null;

    public static string? Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Null when the plan can be made, else the sentence the form shows.</summary>
    public static string? Check(string? text, DateOnly date, DateOnly today)
    {
        var clean = Clean(text);
        if (clean is null)
        {
            return "Say what the plan is.";
        }

        if (clean.Length > LongestText)
        {
            return $"Keep it under {LongestText.ToString(CultureInfo.InvariantCulture)} letters.";
        }

        return date < today ? "That day has gone." : null;
    }

    /// <summary>
    /// D361: the calendar looks <see cref="CalendarItems.DaysAhead"/> days ahead, so a plan saved for a later day is
    /// kept and not shown yet. Null when the day is inside the window; else the sentence for the page, naming the day
    /// the way the page's own day headings do (it is never today, tomorrow or a weekday here).
    /// </summary>
    public static string? SavedNote(DateOnly date, DateOnly today)
    {
        if (date < today.AddDays(CalendarItems.DaysAhead))
        {
            return null;
        }

        var noon = new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
        var label = CalendarScreenDayLabel.For(new CalendarDay(date, []), noon, TimeZoneInfo.Utc);
        return $"Saved for {label}. It will show once it is within two weeks.";
    }

    /// <summary>Just the maker, or every adult.</summary>
    public static IReadOnlyList<ViewerId> Who(ViewerId maker, bool shared, IEnumerable<ViewerId> adults)
    {
        ArgumentNullException.ThrowIfNull(adults);
        return shared ? adults.Append(maker).Distinct().ToList() : [maker];
    }

    public static string NewId() => Guid.NewGuid().ToString("N");
}
