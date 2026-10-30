namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What reading the taste profile gave: none yet, a profile and the people it is about, or a file that is there and cannot be read.</summary>
public abstract record MealTasteRead
{
    private MealTasteRead()
    {
    }

    public sealed record None : MealTasteRead;

    public sealed record Held(TasteProfile Profile, IReadOnlyList<TastePerson> People) : MealTasteRead;

    public sealed record Unreadable(string Reason) : MealTasteRead;
}

/// <summary>Where the taste profile and the roster it is about are read from.</summary>
public interface IMealTasteSource
{
    MealTasteRead Read();
}

/// <summary>The source used until a profile is kept anywhere: there is none, and the planner plans without one.</summary>
public sealed class NoMealTaste : IMealTasteSource
{
    public MealTasteRead Read() => new MealTasteRead.None();
}
