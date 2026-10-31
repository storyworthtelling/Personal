namespace Constitution.Domain.Meals;

/// <summary>Which meal a photograph replaced. It is never asked: the time of day says (D66).</summary>
public enum AteMeal
{
    Breakfast,
    Lunch,
    Dinner,
}

/// <summary>The meal the time of day names, and the word for it.</summary>
public static class AteMeals
{
    /// <summary>The hour before which a photograph is breakfast (D66).</summary>
    public const int LunchFromHour = 11;

    /// <summary>The hour from which a photograph is dinner (D66).</summary>
    public const int DinnerFromHour = 16;

    /// <summary>Before 11 am breakfast, before 4 pm lunch, then dinner (D66). Midnight to 11 am is breakfast, as D66 words it.</summary>
    public static AteMeal At(TimeOnly time)
    {
        if (time.Hour < LunchFromHour)
        {
            return AteMeal.Breakfast;
        }

        return time.Hour < DinnerFromHour ? AteMeal.Lunch : AteMeal.Dinner;
    }

    public static string Word(AteMeal meal) => meal switch
    {
        AteMeal.Breakfast => "breakfast",
        AteMeal.Lunch => "lunch",
        _ => "dinner",
    };

    /// <summary>The card the meal sits on, written the way Today writes a kicker.</summary>
    public static string Title(AteMeal meal) => meal switch
    {
        AteMeal.Breakfast => "Breakfast",
        AteMeal.Lunch => "Lunch",
        _ => "Dinner",
    };
}
