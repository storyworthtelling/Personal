using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.Placement;

/// <summary>What a meal event on the calendar is for. A closed list: each has a frozen word in the event's key.</summary>
public enum MealEventRole
{
    /// <summary>The prep-and-cook block, ending where the family dinner starts.</summary>
    Cook,

    /// <summary>The morning reminder to start the slow cooker, for a slow-cooker meal.</summary>
    StartSlowCooker,
}

/// <summary>A stretch of a day by the wall clock ("seven to nine in the morning"). It stays inside one day.</summary>
public sealed record DayPart
{
    /// <exception cref="ArgumentOutOfRangeException">The length is not positive, or the part would run past midnight.</exception>
    public DayPart(TimeOnly start, TimeSpan length)
    {
        DayWindow.Check(start, length, "A part of the day", nameof(length));
        Start = start;
        Length = length;
    }

    public TimeOnly Start { get; }

    public TimeSpan Length { get; }
}

/// <summary>
/// What meal placement is told that is not in the week of dinners. Nothing here is a household setting yet; the defaults are the
/// agent's numbers, named so that using them is visible (see <see cref="DefaultMornings"/>).
/// </summary>
public sealed record MealPlacementOptions
{
    /// <summary>Ten minutes to switch the slow cooker on. The agent's number, not the owner's.</summary>
    public static readonly TimeSpan DefaultSlowCookerStartLength = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Where the start may go, in order of preference: seven to nine, then six to seven, then nine to noon. The first is the stretch
    /// before an eight o'clock shift; the others are for when it is taken, or for a day at home. The agent's numbers, not the owner's.
    /// </summary>
    public static IReadOnlyList<DayPart> DefaultMornings { get; } =
    [
        new DayPart(new TimeOnly(7, 0), TimeSpan.FromHours(2)),
        new DayPart(new TimeOnly(6, 0), TimeSpan.FromHours(1)),
        new DayPart(new TimeOnly(9, 0), TimeSpan.FromHours(3)),
    ];

    private HashSet<RecipeId> slowCookerCards = [];
    private TimeSpan slowCookerStartLength = DefaultSlowCookerStartLength;
    private IReadOnlyList<DayPart> slowCookerMornings = DefaultMornings;

    /// <summary>
    /// The cards that cook in a slow cooker. The recipe record has no equipment field yet, so the caller says which; none is the
    /// usual case, and then no morning event is ever made.
    /// </summary>
    public IReadOnlySet<RecipeId> SlowCookerCards
    {
        get => slowCookerCards;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            slowCookerCards = [.. value];
        }
    }

    /// <summary>How long the morning event takes.</summary>
    public TimeSpan SlowCookerStartLength
    {
        get => slowCookerStartLength;
        init
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Starting the slow cooker takes some time.");
            }

            slowCookerStartLength = value;
        }
    }

    /// <summary>Where the morning event may go, in order of preference. At least one.</summary>
    public IReadOnlyList<DayPart> SlowCookerMornings
    {
        get => slowCookerMornings;
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Count == 0)
            {
                throw new ArgumentException("A slow cooker start needs at least one part of the morning to go in.", nameof(value));
            }

            slowCookerMornings = value.ToList().AsReadOnly();
        }
    }
}

/// <summary>
/// C-6.4.1 · cooks, blocks and dinner time, as far as the placement engine is concerned: a week of dinners turned into the
/// household events it asks the calendar for. Pure. It decides nothing about who cooks or when (the dinner planner did, and the
/// week carries it); it only says what goes on whose calendar, and where each may go.
///
/// <list type="bullet">
///   <item><b>The cook block</b>, "Cook: &lt;dish&gt;", is on the cook's primary calendar alone, and its one window is exactly the
///   planned block, so it can only be placed ending at the family dinner's start. If the cook's calendar has since filled over
///   it, it is not placed (the engine fails closed); it is never moved earlier to fit.</item>
///   <item><b>The family dinner is not an event.</b> It is the household's never-placed window (<see cref="PlanningRules.Dinner"/>)
///   and the engine would refuse to put anything on it; no line of the PBI asks for one.</item>
///   <item><b>A slow-cooker meal adds "Start the slow cooker"</b> on the cook's calendar, in the morning windows of
///   <see cref="MealPlacementOptions"/>, each cut short of the cook block, tried in order.</item>
///   <item><b>A meal's identity is its evening and its role</b> ("cook-tue", "slowcooker-tue"), never a date: the key is the person,
///   the week and that identity, so an event the owner moves is found again, and a different dish on the same evening is the same
///   event.</item>
///   <item><b>Order.</b> Every cook block, evening by evening, then every morning start: a fixed block claims its minutes
///   before a flexible start can look for any. The engine places household events before workouts.</item>
/// </list>
///
/// <para>A week that does not hold together (a cook who is not an adult, a block that does not end at dinner, a card that is not in
/// the box) is refused with a plain sentence rather than squared: the week has to be planned again. The sentences name the evening
/// and nothing from a calendar.</para>
/// </summary>
public static class MealPlacement
{
    private static readonly string[] DayWords = ["mon", "tue", "wed", "thu", "fri", "sat", "sun"];

    private static readonly string[] DayNames = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

    private const string CookWord = "cook";
    private const string SlowCookerWord = "slowcooker";
    private const string CookTitle = "Cook: ";
    private const string SlowCookerTitle = "Start the slow cooker";

    /// <summary>The household events a week of dinners asks for: every cook block, then every morning start.</summary>
    /// <param name="week">The week. One that was not planned asks for nothing.</param>
    /// <param name="rules">The household's rules: who the adults are, the zone, and when the family dinner is.</param>
    /// <param name="recipes">The box, for the dish names on the calendar.</param>
    /// <param name="options">Which cards are slow-cooker meals and where their morning start may go; null means none are.</param>
    /// <exception cref="ArgumentException">The week does not hold together (see the type).</exception>
    public static IReadOnlyList<HouseholdEventRequest> Requests(
        MealWeek week,
        PlanningRules rules,
        IReadOnlyList<Recipe> recipes,
        MealPlacementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(recipes);
        if (!week.IsPlanned)
        {
            return [];
        }

        var chosen = options ?? new MealPlacementOptions();
        var blocks = new List<HouseholdEventRequest>();
        var starts = new List<HouseholdEventRequest>();
        foreach (var dinner in week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
        {
            var evening = dinner.Day.DayNumber - week.WeekStart.DayNumber;
            var name = DayNames[evening];
            var adult = rules.FindAdult(dinner.Cook!)
                ?? throw new ArgumentException($"{name}'s cook is not one of the household's adults, so no cook block was made.", nameof(week));
            var card = recipes.FirstOrDefault(item => item.Id == dinner.Recipe)
                ?? throw new ArgumentException($"{name}'s dinner names a card that is not in the box.", nameof(recipes));
            var dinnerStart = MealFacts.DinnerWindow(rules, dinner.Day).Start;
            if (dinner.CookEnd != dinnerStart)
            {
                throw new ArgumentException($"{name}'s cook block does not end where the family dinner starts, so the week has to be planned again.", nameof(week));
            }

            var cookStart = dinner.CookStart!.Value;
            blocks.Add(new HouseholdEventRequest(
                Identity(MealEventRole.Cook, evening),
                CookTitle + card.Title,
                dinnerStart - cookStart,
                [adult.Name],
                [new PlacementWindow(cookStart, dinnerStart)]));

            if (chosen.SlowCookerCards.Contains(card.Id))
            {
                starts.Add(new HouseholdEventRequest(
                    Identity(MealEventRole.StartSlowCooker, evening),
                    SlowCookerTitle,
                    chosen.SlowCookerStartLength,
                    [adult.Name],
                    MorningWindows(dinner.Day, cookStart, rules, chosen, name)));
            }
        }

        return [.. blocks, .. starts];
    }

    /// <summary>The week handed to the placement engine: the meals as household events (which the engine places ahead of any workout), the adults' time, and the workouts to go around them.</summary>
    /// <exception cref="ArgumentException">The week does not hold together (see <see cref="Requests"/>).</exception>
    public static PlacementRequest Request(
        MealWeek week,
        PlanningRules rules,
        IReadOnlyList<Recipe> recipes,
        IReadOnlyList<BusyTime> people,
        HouseholdWorkoutPlan? workouts = null,
        MealPlacementOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(people);
        return new PlacementRequest(week.WeekStart, people, Requests(week, rules, recipes, options), workouts);
    }

    /// <summary>
    /// The key a meal's event is written under, for whoever needs to find it again (the recipe page names the "Start the slow cooker"
    /// event). The same key the engine derives from <see cref="Requests"/>; it holds no date.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The day is not one of the seven evenings of the week.</exception>
    public static PlacementKey KeyOf(MealEventRole role, string person, DateOnly weekStart, DateOnly day)
    {
        var evening = day.DayNumber - weekStart.DayNumber;
        if (evening is < 0 or >= MealWeek.Evenings)
        {
            throw new ArgumentOutOfRangeException(nameof(day), day, "The day is not in that week.");
        }

        return PlacementKey.ForHousehold(person, PlacementKey.WeekIndexOf(weekStart), Identity(role, evening));
    }

    private static List<PlacementWindow> MorningWindows(DateOnly day, DateTimeOffset cookStart, PlanningRules rules, MealPlacementOptions options, string name)
    {
        var windows = new List<PlacementWindow>();
        foreach (var part in options.SlowCookerMornings)
        {
            var (open, close) = ZoneClock.Window(day, part.Start, part.Length, rules.Zone);
            var end = close < cookStart ? close : cookStart;
            if (end - open >= options.SlowCookerStartLength)
            {
                windows.Add(new PlacementWindow(open, end));
            }
        }

        if (windows.Count == 0)
        {
            throw new ArgumentException($"There is no room before {name}'s cook block for a slow cooker start in the morning.", nameof(options));
        }

        return windows;
    }

    // The frozen spelling of an identity: the role's word and the weekday, never the date. Written out so that renaming an enum
    // member cannot rename a key already stored on the owner's calendar.
    private static string Identity(MealEventRole role, int evening) => role switch
    {
        MealEventRole.Cook => CookWord + "-" + DayWords[evening],
        MealEventRole.StartSlowCooker => SlowCookerWord + "-" + DayWords[evening],
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "This kind of meal event has no frozen name in the calendar key. Give it one on purpose."),
    };
}
