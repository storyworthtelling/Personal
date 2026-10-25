using Constitution.Domain.Scraps;
using Constitution.Domain.Voice;

namespace Constitution.Domain.Meals.Screens;

/// <summary>
/// Tonight's dinner as Today's scrap says it (C-4.1.3, book p.11): an index card with the dish and who cooks, or, when the
/// household is eating out, a restaurant receipt (D46) since it is not a recipe. <see cref="Paper"/> is the scrap's kind.
/// </summary>
public sealed record DinnerClipping(ScrapKind Paper, string Kicker, string Headline, string? Line)
{
    /// <summary>The card for the day, or null when there is no readable week or it holds no evening for that day.</summary>
    public static DinnerClipping? For(MealWeekScreenRead read, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read is not MealWeekScreenRead.Ready { Snapshot.Week.IsPlanned: true } ready)
        {
            return null;
        }

        var snapshot = ready.Snapshot;
        var dinner = snapshot.Week.Dinners.FirstOrDefault(choice => choice.Day == today);
        if (dinner is null)
        {
            return null;
        }

        switch (dinner.Kind)
        {
            case DinnerKind.Cooked:
                var cardLine = dinner.Cook + " cooks";
                var title = dinner.Recipe is { } id && snapshot.Dishes.TryGetValue(id, out var facts) ? facts.Title : MealsWeekModel.MissingDishWords;
                return new DinnerClipping(ScrapKind.IndexCard, "Dinner · " + SeatWords.Clock(TimeOnly.FromTimeSpan(dinner.CookEnd!.Value.TimeOfDay)), title, cardLine);
            case DinnerKind.EatOut:
                return snapshot.EatingOut is { } map && map.TryGetValue(today, out var detail)
                    ? new DinnerClipping(
                        ScrapKind.Receipt,
                        "Dinner · " + SeatWords.Clock(detail.DinnerAt),
                        "Out · " + detail.Place,
                        "Leave " + SeatWords.Clock(detail.LeaveAt) + " · " + detail.Who)
                    : new DinnerClipping(ScrapKind.Receipt, "Dinner · out", "Out tonight", dinner.Reason);
            case DinnerKind.Leftovers:
                return new DinnerClipping(ScrapKind.IndexCard, "Dinner", "Leftovers", dinner.Reason);
            default:
                return new DinnerClipping(ScrapKind.IndexCard, "Dinner", "Nothing planned", dinner.Reason);
        }
    }

    /// <summary>The dinner the top line's seat is composed from: the dish, or "out"; null when the week says nothing about tonight.</summary>
    public static DinnerPlan? PlanOf(MealWeekScreenRead read, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (read is not MealWeekScreenRead.Ready { Snapshot.Week.IsPlanned: true } ready)
        {
            return null;
        }

        var dinner = ready.Snapshot.Week.Dinners.FirstOrDefault(choice => choice.Day == today);
        if (dinner is null)
        {
            return null;
        }

        return dinner.Kind switch
        {
            DinnerKind.EatOut => DinnerPlan.Out,
            DinnerKind.Cooked when dinner.Recipe is { } id && ready.Snapshot.Dishes.TryGetValue(id, out var facts) => DinnerPlan.At(facts.Title),
            _ => null,
        };
    }
}
