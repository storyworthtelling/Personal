using Constitution.Domain.Household;
using Constitution.Domain.Household.Records;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Infrastructure.Household;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The taste profile the planner and the rating page are given (D133), built afresh each read from what the household file keeps: the roster, the box, the
/// <c>rating</c> records the read-back writes and the <c>taste-note</c> records the deadline step teaches (<see cref="TasteMaps"/>). Read from the phone's own copy, as the
/// recipe box is.
///
/// <para><b>With nothing to go on it is today's behaviour:</b> no household yet, no one on the roster, or no rating and no note is <see cref="MealTasteRead.None"/>, which
/// plans without a profile. A file that will not open is <see cref="MealTasteRead.Unreadable"/> with the file's own sentence (the box beside it stops the run in the same case).
/// A single rating or note this build cannot read (most likely a newer writer's) is left out and left exactly as it is; the rest still count. Nothing here writes.</para>
/// </summary>
public sealed class TasteFromHousehold(HouseholdFileService service) : IMealTasteSource
{
    public MealTasteRead Read()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new MealTasteRead.Unreadable(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new MealTasteRead.Unreadable(HouseholdSentences.LocalDamaged);
        }

        var people = RosterView.From(document).Entries.Select(TastePerson.From).ToList();
        var ratings = HouseholdRecords.Read(document, TasteMaps.Rating).Items;
        var notes = HouseholdRecords.Read(document, TasteMaps.Note).Items;
        if (people.Count == 0 || (ratings.Count == 0 && notes.Count == 0))
        {
            return new MealTasteRead.None();
        }

        var cards = HouseholdRecords.Read(document, RecipeMaps.Recipe).Items;
        return new MealTasteRead.Held(TasteProfile.Build(people, cards, ratings, notes), people);
    }
}
