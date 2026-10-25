using Constitution.Domain.Calendar;
using Constitution.Domain.Placement;

namespace Constitution.Domain.Groceries;

/// <summary>What the shopping trip is told that is not on a calendar. The numbers are the agent's, named so that using them is visible.</summary>
public sealed record GroceryTripOptions
{
    /// <summary>An hour for the trip.</summary>
    public static readonly TimeSpan DefaultLength = TimeSpan.FromMinutes(60);

    /// <summary>Nine in the morning to five in the afternoon, before the family dinner.</summary>
    public static readonly DayPart DefaultDaytime = new(new TimeOnly(9, 0), TimeSpan.FromHours(8));

    /// <summary>The trip reaches back at most this many days from the Sunday the week opens on (the Thursday, Friday and Saturday before it).</summary>
    public const int DefaultDaysBefore = 3;

    private TimeSpan length = DefaultLength;
    private int daysBefore = DefaultDaysBefore;

    public TimeSpan Length
    {
        get => length;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "A shopping trip takes some time.");
            }

            length = value;
        }
    }

    /// <summary>The part of each day a trip may go in.</summary>
    public DayPart Daytime { get; init; } = DefaultDaytime;

    public int DaysBefore
    {
        get => daysBefore;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            daysBefore = value;
        }
    }
}

/// <summary>
/// C-6.4.2 · the "Groceries" trip, as far as the placement engine is concerned. Pure. One adult shops each week, <b>alternating
/// by week number</b> (an even week the first adult, an odd one the next), <b>unless that one is busy</b>: busy meaning no room for the trip
/// anywhere in its windows, or a calendar nobody could read (which is never taken as free time). Then the next adult is asked, and if
/// nobody has room the trip stays with the one whose week it is, so the engine says plainly that it could not be placed.
///
/// <para>The trip goes <b>after approval and before the week opens</b>: one window per day from the day of approval to the Saturday, the
/// daytime of each day cut to start no earlier than the approval, tried latest day first (the freshest shop). Its identity is
/// "groceries", the same every week, so the key (person, week, identity) never holds a date and a trip the owner moves is found again.</para>
/// </summary>
public static class GroceryPlacement
{
    /// <summary>The frozen identity of the trip in the calendar key.</summary>
    public const string Identity = "groceries";

    public const string Title = "Groceries";

    /// <param name="weekStart">The Sunday the week being shopped for opens on (<see cref="PlanningWeek"/>). Its week number decides whose turn it is.</param>
    /// <param name="people">Each adult's busy time, covering the days before the Sunday. Anyone not here cannot shop.</param>
    /// <param name="approvedAt">When the week was approved; the trip is never before it.</param>
    /// <returns>The request, or null when no daytime is left between approval and the week.</returns>
    /// <exception cref="ArgumentException">The week does not begin on a Sunday.</exception>
    public static HouseholdEventRequest? Request(
        DateOnly weekStart,
        PlanningRules rules,
        IReadOnlyList<BusyTime> people,
        DateTimeOffset approvedAt,
        GroceryTripOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(people);
        PlanningWeek.RequireStart(weekStart, nameof(weekStart));

        var chosen = options ?? new GroceryTripOptions();
        var windows = Windows(weekStart, rules, approvedAt, chosen);
        if (windows.Count == 0)
        {
            return null;
        }

        var turn = PlacementKey.WeekIndexOf(weekStart) % rules.Adults.Count;
        var order = Enumerable.Range(0, rules.Adults.Count).Select(offset => rules.Adults[(turn + offset) % rules.Adults.Count]).ToList();
        var shopper = order.FirstOrDefault(adult => HasRoom(adult.Name, people, windows, chosen.Length)) ?? order[0];
        return new HouseholdEventRequest(Identity, Title, chosen.Length, [shopper.Name], windows);
    }

    private static List<PlacementWindow> Windows(DateOnly weekStart, PlanningRules rules, DateTimeOffset approvedAt, GroceryTripOptions options)
    {
        var windows = new List<PlacementWindow>();
        var approvalDay = ZoneClock.DayOf(approvedAt, rules.Zone);
        for (var back = 1; back <= options.DaysBefore; back++)
        {
            var day = weekStart.AddDays(-back);
            if (day < approvalDay)
            {
                break;
            }

            var (open, close) = ZoneClock.Window(day, options.Daytime.Start, options.Daytime.Length, rules.Zone);
            var start = open > approvedAt ? open : approvedAt;
            if (close - start >= options.Length)
            {
                windows.Add(new PlacementWindow(start, close));
            }
        }

        return windows;
    }

    private static bool HasRoom(string person, IReadOnlyList<BusyTime> people, List<PlacementWindow> windows, TimeSpan length)
    {
        var busy = people.FirstOrDefault(item => string.Equals(item.Person, person, StringComparison.OrdinalIgnoreCase));
        return busy is { IsComplete: true } && windows.Any(window => busy.FreeGaps(window.Start, window.End, length).Count > 0);
    }
}
