using Constitution.Domain.Meals.Pipeline;
using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Who eats, over the household file: the people the roster lists (<see cref="MealRosterReading"/> says when that is not enough to go on). A file that will not open,
/// or a copy that is damaged, says so in a fixed sentence and gives no people, so the documents are not made on a guessed headcount.
/// </summary>
public sealed class HouseholdMealRoster(HouseholdFileService service) : IMealRosterSource
{
    public MealRosterRead Read()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return MealRosterReading.From(null, blocked);
        }

        try
        {
            service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return MealRosterReading.From(null, HouseholdSentences.LocalDamaged);
        }

        return MealRosterReading.From(service.Roster, null);
    }
}

/// <summary>
/// What is on hand, over the household file: the <c>staple</c> records read through <see cref="Constitution.Domain.Household.Records.PantryMaps"/>
/// (<see cref="MealPantryReading"/> decides what that comes to). No household file yet is "none yet", which the run answers with an empty pantry and says so;
/// a file that will not open is "unreadable", which stops the documents, because an unreadable pantry is not an empty one.
/// </summary>
public sealed class HouseholdMealPantry(HouseholdFileService service) : IMealPantrySource
{
    public MealPantryRead Read()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new MealPantryRead.Unreadable(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new MealPantryRead.Unreadable(HouseholdSentences.LocalDamaged);
        }

        var unmet = service.Standing.State is HouseholdFileState.NotCompared or HouseholdFileState.LocalOnly or HouseholdFileState.WaitingForShare;
        return MealPantryReading.From(document, fileMet: !unmet);
    }
}
