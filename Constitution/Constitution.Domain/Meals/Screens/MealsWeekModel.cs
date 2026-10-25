using System.Globalization;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Meals.Screens;

/// <summary>What the This week view is showing.</summary>
public enum WeekState
{
    Planned,
    NoPlan,
    NotEnoughRecipes,
    CalendarUnreadable,
    Unavailable,
}

/// <summary>Tonight's large card, as words.</summary>
public sealed record TonightCard(string Kicker, string Dish, string? CookLine, DinnerKind Kind);

/// <summary>One ruled line of the week card. Markers are the small words beside a dish ("slow cooker", "new").</summary>
public sealed record WeekLine(
    DateOnly Day,
    string DayLabel,
    string RelativeLabel,
    bool IsToday,
    bool IsPast,
    DinnerKind Kind,
    bool IsOut,
    string Dish,
    string? Cook,
    IReadOnlyList<string> Markers,
    string? ReasonLine)
{
    /// <summary>No evening can be swapped from this view (brief): tonight's least of all.</summary>
    public bool CanBeSwapped => false;
}

/// <summary>
/// The Meals tab's first view as a pure model (C-6.7.1, book p.13): tonight on a large card, then the week on one card, a ruled
/// line a day with today's in bold. Every sentence is here; the head paints. The week reads Monday to Sunday as drawn.
/// </summary>
public sealed class MealsWeekModel
{
    public const string NoPlanSentence = "No dinners are planned yet.";

    public const string NothingTonightSentence = "Nothing is planned for tonight.";

    public const string MissingDishWords = "A card no longer in the box";

    /// <summary>The bar's action, kept from Today (book p.13); it is built with F-6.6.</summary>
    public const string ActionLabel = "I ate something else";

    private MealsWeekModel(
        WeekState state,
        string status,
        string heading,
        TonightCard? tonight,
        IReadOnlyList<WeekLine> days,
        IReadOnlyList<string> notes)
    {
        State = state;
        Status = status;
        WeekHeading = heading;
        Tonight = tonight;
        Days = days;
        Notes = notes;
    }

    public WeekState State { get; }

    /// <summary>The line the view prints in place of the cards when there is no week; the week's own heading otherwise.</summary>
    public string Status { get; }

    public string WeekHeading { get; }

    public TonightCard? Tonight { get; }

    public string TonightSentence => Tonight is null ? NothingTonightSentence : Tonight.Dish;

    public IReadOnlyList<WeekLine> Days { get; }

    public IReadOnlyList<string> Notes { get; }

    public bool TonightCanBeSwapped => false;

    public static MealsWeekModel Build(MealWeekScreenRead read, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(read);
        return read switch
        {
            MealWeekScreenRead.Ready ready => FromSnapshot(ready.Snapshot, today),
            MealWeekScreenRead.Unavailable unavailable => Without(WeekState.Unavailable, unavailable.Reason),
            _ => Without(WeekState.NoPlan, NoPlanSentence),
        };
    }

    private static MealsWeekModel Without(WeekState state, string sentence) => new(state, sentence, string.Empty, null, [], []);

    private static MealsWeekModel FromSnapshot(MealWeekSnapshot snapshot, DateOnly today)
    {
        var week = snapshot.Week;
        if (week.Stop is { } stop)
        {
            var state = stop == MealStop.BoxTooSmall ? WeekState.NotEnoughRecipes : WeekState.CalendarUnreadable;
            return Without(state, week.StopReason!);
        }

        var days = week.Dinners.Select(dinner => Line(snapshot, dinner, today)).ToList();
        var tonight = week.Dinners.Where(dinner => dinner.Day == today).Select(dinner => TonightOf(snapshot, dinner)).FirstOrDefault();
        var heading = snapshot.ApprovedOn is { } approved ? "This week · approved " + approved.DayOfWeek.ToString() : "This week";
        return new MealsWeekModel(WeekState.Planned, heading, heading, tonight, days, week.Notes);
    }

    private static string DishTitle(MealWeekSnapshot snapshot, DinnerChoice dinner) =>
        dinner.Recipe is { } id && snapshot.Dishes.TryGetValue(id, out var facts) ? facts.Title : MissingDishWords;

    private static EatOutDetail? OutDetail(MealWeekSnapshot snapshot, DinnerChoice dinner) =>
        snapshot.EatingOut is { } map && map.TryGetValue(dinner.Day, out var detail) ? detail : null;

    private static TimeOnly Time(DateTimeOffset moment) => TimeOnly.FromTimeSpan(moment.TimeOfDay);

    private static TonightCard TonightOf(MealWeekSnapshot snapshot, DinnerChoice dinner)
    {
        switch (dinner.Kind)
        {
            case DinnerKind.Cooked:
                return new TonightCard(
                    "Tonight · " + SeatWords.Clock(Time(dinner.CookEnd!.Value)),
                    DishTitle(snapshot, dinner),
                    dinner.Cook + " cooks · start " + SeatWords.Clock(Time(dinner.CookStart!.Value)),
                    dinner.Kind);
            case DinnerKind.EatOut:
                var detail = OutDetail(snapshot, dinner);
                return detail is null
                    ? new TonightCard("Tonight", "Out", dinner.Reason, dinner.Kind)
                    : new TonightCard("Tonight · " + SeatWords.Clock(detail.DinnerAt), "Out · " + detail.Place, "Leave " + SeatWords.Clock(detail.LeaveAt) + " · " + detail.Who, dinner.Kind);
            case DinnerKind.Leftovers:
                return new TonightCard("Tonight", "Leftovers", dinner.Reason, dinner.Kind);
            default:
                return new TonightCard("Tonight", "Nothing planned", dinner.Reason, dinner.Kind);
        }
    }

    private static WeekLine Line(MealWeekSnapshot snapshot, DinnerChoice dinner, DateOnly today)
    {
        var markers = new List<string>();
        var dish = dinner.Kind switch
        {
            DinnerKind.Cooked => DishTitle(snapshot, dinner),
            DinnerKind.EatOut => OutDetail(snapshot, dinner) is { } detail ? "Out · " + detail.Place : "Out",
            DinnerKind.Leftovers => "Leftovers",
            _ => "Nothing planned",
        };
        if (dinner.Kind == DinnerKind.Cooked && dinner.Recipe is { } id && snapshot.Dishes.TryGetValue(id, out var facts))
        {
            if (facts.SlowCooker)
            {
                markers.Add("slow cooker");
            }

            if (facts.IsNew)
            {
                markers.Add("new");
            }
        }

        return new WeekLine(
            dinner.Day,
            dinner.Day.ToString("ddd", CultureInfo.InvariantCulture),
            Relative(dinner.Day, today),
            dinner.Day == today,
            dinner.Day < today,
            dinner.Kind,
            dinner.Kind == DinnerKind.EatOut,
            dish,
            dinner.Kind == DinnerKind.Cooked ? dinner.Cook : null,
            markers,
            dinner.Kind == DinnerKind.Cooked ? null : dinner.Reason);
    }

    private static string Relative(DateOnly day, DateOnly today) => (day.DayNumber - today.DayNumber) switch
    {
        0 => "Today",
        1 => "Tomorrow",
        -1 => "Yesterday",
        _ => day.ToString("dddd", CultureInfo.InvariantCulture),
    };
}
