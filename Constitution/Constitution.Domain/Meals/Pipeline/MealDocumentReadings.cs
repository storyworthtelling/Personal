using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Groceries;
using Storyworthtelling.Household;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>
/// Turns the roster the household file gave into what the meal documents read. Pure. <b>It fails closed:</b> a roster that is missing, empty or
/// short a person is no roster, because a headcount that quietly leaves someone out scales every recipe for fewer mouths and asks no one's opinion of the dish.
/// Every sentence quotes nothing the household wrote.
/// </summary>
public static class MealRosterReading
{
    public const string NoRoster = "There is no household roster on this phone yet, so nobody knows who is eating.";
    public const string EmptyRoster = "The household roster has no one on it, so nobody knows who is eating.";
    public const string ShortRoster = "A person on the household roster cannot be read, so a headcount would leave someone out.";

    public static MealRosterRead From(RosterView? view, string? blockedSentence)
    {
        if (!string.IsNullOrWhiteSpace(blockedSentence))
        {
            return new MealRosterRead.Unavailable(blockedSentence);
        }

        if (view is null)
        {
            return new MealRosterRead.Unavailable(NoRoster);
        }

        if (view.Unreadable.Count > 0)
        {
            return new MealRosterRead.Unavailable(ShortRoster);
        }

        return view.Entries.Count == 0 ? new MealRosterRead.Unavailable(EmptyRoster) : new MealRosterRead.Ready(view.Entries);
    }
}

/// <summary>Turns the household file into the pantry the meal sheet reads. Pure.</summary>
public static class MealPantryReading
{
    public const string NoFile = "There is no household file on this phone yet, so the sheet's groceries are listed with nothing on hand.";
    public const string Duplicate = "The pantry lists one ingredient twice and cannot be read as it stands.";

    /// <param name="document">The phone's copy of the household file.</param>
    /// <param name="fileMet">False while this phone has not met the household file (an empty copy then means "no file yet", not "nothing on the shelf").</param>
    public static MealPantryRead From(HouseholdDocument document, bool fileMet)
    {
        ArgumentNullException.ThrowIfNull(document);
        var staples = HouseholdRecords.Read(document, PantryMaps.Staple);
        if (staples.Items.Count == 0 && staples.Unreadable.Count == 0 && !fileMet)
        {
            return new MealPantryRead.NoneYet(NoFile);
        }

        try
        {
            return new MealPantryRead.Ready(Pantry.From(staples.Items), staples.Unreadable.Count);
        }
        catch (ArgumentException)
        {
            return new MealPantryRead.Unreadable(Duplicate);
        }
    }
}
