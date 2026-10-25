using Constitution.Domain.Calendar;

namespace Constitution.Domain.Meals;

/// <summary>Why no dinners were planned at all. A closed list; the sentence beside it is the plain-words version.</summary>
public enum MealStop
{
    /// <summary>The box holds fewer than ten cards (<see cref="Constitution.Domain.Recipes.EmptyBox.Target"/>).</summary>
    BoxTooSmall,

    /// <summary>A calendar could not be read, or does not cover the week. Failing closed: unreadable is never "nothing on".</summary>
    CalendarUnreadable,
}

/// <summary>
/// The week's dinners: seven evenings from the Sunday the planning week opens on (<see cref="PlanningWeek"/>), or nothing and the reason. This is the page model; it carries no origin
/// (see <see cref="MealOutcome"/>). Notes are week-level plain sentences ("no stretch dish this week").
/// </summary>
public sealed record MealWeek
{
    public const int Evenings = 7;

    private MealWeek(DateOnly weekStart, IReadOnlyList<DinnerChoice> dinners, MealStop? stop, string? stopReason, IReadOnlyList<string> notes)
    {
        WeekStart = weekStart;
        Dinners = dinners;
        Stop = stop;
        StopReason = stopReason;
        Notes = notes;
    }

    public DateOnly WeekStart { get; }

    /// <summary>Seven choices, one per evening from <see cref="WeekStart"/>; empty when <see cref="Stop"/> is set.</summary>
    public IReadOnlyList<DinnerChoice> Dinners { get; }

    public MealStop? Stop { get; }

    /// <summary>The plain sentence for <see cref="Stop"/>; contains nothing from a calendar.</summary>
    public string? StopReason { get; }

    public IReadOnlyList<string> Notes { get; }

    public bool IsPlanned => Stop is null;

    public static MealWeek Planned(DateOnly weekStart, IEnumerable<DinnerChoice> dinners, IEnumerable<string>? notes = null)
    {
        ArgumentNullException.ThrowIfNull(dinners);
        var list = dinners.ToList();
        PlanningWeek.RequireStart(weekStart, nameof(weekStart));
        if (list.Count != Evenings || list.Select((dinner, index) => dinner.Day == weekStart.AddDays(index)).Contains(false))
        {
            throw new ArgumentException("A planned week has one dinner for each of the seven evenings, in order.", nameof(dinners));
        }

        return new MealWeek(weekStart, list.AsReadOnly(), null, null, (notes ?? []).ToList().AsReadOnly());
    }

    public static MealWeek NotPlanned(DateOnly weekStart, MealStop stop, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new MealWeek(weekStart, [], stop, reason.Trim(), []);
    }
}
