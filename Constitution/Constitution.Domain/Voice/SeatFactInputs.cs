namespace Constitution.Domain.Voice;

/// <summary>
/// Tonight's dinner as the Today lines see it: a dish cooked at home, or a night out. Nothing else
/// about dinner (recipe, shopping, who cooks) is a seat's business.
/// </summary>
public sealed record DinnerPlan
{
    private DinnerPlan(string? dish) => Dish = dish;

    /// <summary>The dish, or null on a night out.</summary>
    public string? Dish { get; }

    public bool IsOut => Dish is null;

    public static DinnerPlan Out { get; } = new((string?)null);

    public static DinnerPlan At(string dish)
    {
        if (string.IsNullOrWhiteSpace(dish))
        {
            throw new ArgumentException("A dinner at home names its dish.", nameof(dish));
        }

        return new DinnerPlan(dish.Trim());
    }
}

/// <summary>Today on a day with a workout: what and when and for how long, and tonight's dinner.</summary>
public sealed record TodayFacts(TimeOnly WorkoutStart, string WorkoutKind, int WorkoutMinutes, DinnerPlan Dinner);

/// <summary>Today on a rest day: only tonight's dinner.</summary>
public sealed record RestDayFacts(DinnerPlan Dinner);

/// <summary>What a signal is measured in; each unit knows how it is said and how big a wobble is not a move.</summary>
public enum SignalUnit
{
    Hours,
    Pounds,
    BeatsPerMinute,
    Milliseconds,
    Steps,
    Minutes,
    Percent,
}

/// <summary>
/// One signal's month against last month: both averages in the same unit. <paramref name="RisingIsWorse"/>
/// is what makes a rising weight worsening (D70); <paramref name="NamePlural"/> only decides "Steps are"
/// against "Sleep is".
/// </summary>
public sealed record SignalMonth(
    string Name, SignalUnit Unit, double ThisMonth, double LastMonth, bool RisingIsWorse, bool NamePlural = false);

/// <summary>
/// Body's monthly sentence: the signals' months, or, the day after a device skipped a day, the name of
/// that device (which replaces the month for the day: it is not a seventh seat).
/// </summary>
public sealed record BodyMonthFacts(IReadOnlyList<SignalMonth> Signals, string? SkippedDevice = null);

/// <summary>
/// A signal's own sentence: the latest reading against the owner's own past average. There is
/// deliberately nowhere on this type to put a norm, a target or a population figure.
/// </summary>
public sealed record SignalFacts(
    string Name, SignalUnit Unit, double Latest, string LatestWhen, double PastAverage, string PastLabel, bool RisingIsWorse);

/// <summary>A lift's load today against last time; only an increase is news.</summary>
public sealed record ProgressionFacts(string Exercise, double LoadToday, double LoadLast, string Unit = "lb");

/// <summary>The week of the dinner sheet: which Monday it starts, the nights with a new recipe, the nights out.</summary>
public sealed record MealWeekFacts(DateOnly WeekOf, IReadOnlyList<DayOfWeek> NewRecipeDays, IReadOnlyList<DayOfWeek> DinnerOutDays);
