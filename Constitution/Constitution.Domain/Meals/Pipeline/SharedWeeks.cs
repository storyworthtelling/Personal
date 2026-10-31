using Constitution.Domain.Delivery;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>A week another phone published that has something waiting for this phone's tablet. <paramref name="Sheet"/>: the sheet, not only a recipe.</summary>
public sealed record SharedWeekDue(DateOnly Week, bool Sheet);

/// <summary>
/// Whose week a phone works from, and when a week the lease holder published has something for this phone's tablet (D127). Pure: the moment, the plans and a lookup of what is already
/// on the tablet are handed in; nothing here writes.
///
/// <para><b>Whose.</b> A record this phone wrote is its own mirror and adds nothing; a record another phone wrote is the household's word on that week, and it replaces this phone's own
/// kept copy of the same week for delivery (<see cref="KeptAfter"/>). Which of two records of one week stands is not decided here: the household file's merge decides it (the newer stamp,
/// ties by writer), and only the standing record is ever read.</para>
///
/// <para><b>When.</b> The sheet is due while it is not on the tablet and the deadline is ahead, as <see cref="MealRedeliveryRules"/> sends it; a recipe is due in its hour
/// (<see cref="MealRecipeHour"/>, the one rule the planning phone's pass asks too). The record of deliveries is asked about the sheet always and about a recipe only when its hour is open,
/// so a pass outside every hour reads no more than before.</para>
/// </summary>
public static class SharedWeeks
{
    /// <summary>The plans another phone wrote, in week order. A plan this phone wrote itself is not here.</summary>
    public static IReadOnlyList<SharedWeekPlan> FromOthers(WeekPlanSet set, string me)
    {
        ArgumentNullException.ThrowIfNull(set);
        ArgumentException.ThrowIfNullOrWhiteSpace(me);
        return set.Plans.Where(held => !string.Equals(held.Writer, me, StringComparison.Ordinal)).Select(held => held.Plan).OrderBy(plan => plan.WeekStart).ToList();
    }

    /// <summary>This phone's own kept weeks without the weeks another phone has published, which are theirs to deliver. An unreadable or empty record is returned as it is.</summary>
    public static MealWeekRecordRead KeptAfter(MealWeekRecordRead local, WeekPlanSet set, string me)
    {
        ArgumentNullException.ThrowIfNull(local);
        if (local is not MealWeekRecordRead.Held held)
        {
            return local;
        }

        var theirs = FromOthers(set, me).Select(plan => plan.WeekStart).ToHashSet();
        return theirs.Count == 0 ? local : new MealWeekRecordRead.Held(new MealWeekRecord(held.Record.Weeks.Where(week => !theirs.Contains(week.WeekStart)).ToList()));
    }

    /// <summary>
    /// The weeks with a page waiting for <paramref name="person"/>'s tablet, in week order. No person means no tablet, so none. <paramref name="delivered"/> looks a page up in the record of
    /// what has gone to the tablet.
    /// </summary>
    public static IReadOnlyList<SharedWeekDue> Due(
        IReadOnlyList<SharedWeekPlan> plans, DateTimeOffset now, string? person, Func<string, DeliveredMealDocument?> delivered)
    {
        ArgumentNullException.ThrowIfNull(plans);
        ArgumentNullException.ThrowIfNull(delivered);
        if (string.IsNullOrWhiteSpace(person))
        {
            return [];
        }

        var due = new List<SharedWeekDue>();
        foreach (var plan in plans.OrderBy(item => item.WeekStart))
        {
            var sheet = now < plan.Deadline && delivered(DeliveredName.For(DeliveryKind.Meals, plan.WeekStart)) is null;
            var recipes = MealRecipeHour.DueWeeks(new MealWeekRecordRead.Held(new MealWeekRecord([plan.Stored])), now, person, delivered).Count > 0;
            if (sheet || recipes)
            {
                due.Add(new SharedWeekDue(plan.WeekStart, sheet));
            }
        }

        return due;
    }
}

/// <summary>Joins what the two halves of the recipes part did (this phone's own kept weeks, and the weeks another phone published) into the one outcome the pass keeps.</summary>
public static class MealRecipesOutcomes
{
    /// <summary>A send wins and the weeks of two sends join; a stop is never hidden by a quiet half; two quiet halves are quiet.</summary>
    public static MealRecipesOutcome Combine(MealRecipesOutcome first, MealRecipesOutcome second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        switch (first, second)
        {
            case (MealRecipesOutcome.Done one, MealRecipesOutcome.Done two):
                return new MealRecipesOutcome.Done(one.Weeks.Concat(two.Weeks).OrderBy(week => week.Week).ToList());
            case (MealRecipesOutcome.Done, _):
                return first;
            case (_, MealRecipesOutcome.Done):
                return second;
            case (MealRecipesOutcome.Stopped, _):
                return first;
            case (_, MealRecipesOutcome.Stopped):
                return second;
            default:
                return first;
        }
    }
}
