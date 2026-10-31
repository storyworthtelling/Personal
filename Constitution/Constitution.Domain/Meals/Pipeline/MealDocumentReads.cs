using Constitution.Domain.Groceries;
using Constitution.Domain.Household;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What reading the household roster gave for the meal documents: the people, or the fixed sentence for why there are none to use.</summary>
public abstract record MealRosterRead
{
    private MealRosterRead()
    {
    }

    /// <summary>The people at the table, adults and children, in the order the roster lists them.</summary>
    public sealed record Ready(IReadOnlyList<RosterEntry> People) : MealRosterRead;

    /// <summary>The roster cannot be read now. The sentence quotes nothing the household wrote.</summary>
    public sealed record Unavailable(string Reason) : MealRosterRead;
}

/// <summary>Where the meal documents read who eats. The household file fills this; <b>nothing guesses a headcount</b>.</summary>
public interface IMealRosterSource
{
    MealRosterRead Read();
}

/// <summary>What reading the pantry gave: the pantry (and how many staple records were left out for being unreadable), none yet, or one that is there and cannot be read.</summary>
public abstract record MealPantryRead
{
    private MealPantryRead()
    {
    }

    public sealed record Ready(Pantry Pantry, int UnreadableRecords) : MealPantryRead;

    /// <summary>There is no household file to keep a pantry in yet. The sheet is built on an empty pantry and the trail says so.</summary>
    public sealed record NoneYet(string Sentence) : MealPantryRead;

    /// <summary>A pantry is there and cannot be read. Read as empty it would list what is on the shelf and drop the staples somebody ticked.</summary>
    public sealed record Unreadable(string Reason) : MealPantryRead;
}

/// <summary>Where the meal sheet reads what is on hand and which staples are out.</summary>
public interface IMealPantrySource
{
    MealPantryRead Read();
}
