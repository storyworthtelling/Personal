using Constitution.Domain.Household.Records;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Storyworthtelling.Household;

namespace Constitution.Infrastructure.Household;

/// <summary>
/// The recipe box over the household file (C-6.2): the cards are the file's <c>recipe</c> records read through
/// <see cref="RecipeMaps"/>, and a cuisine move is <see cref="CuisineMove.Apply"/> written to the phone's copy and published by the
/// next pass. Everything the file cannot do is a sentence, never an empty box: a file that will not open says why, a phone that has
/// not met a household file says what it is waiting on, and a damaged copy says so. No cook history or plan is written by anyone yet,
/// so the snapshot carries none, and there is no planner to place a dish.
/// </summary>
public sealed class HouseholdRecipeBox(HouseholdFileService service) : IRecipeBoxSource
{
    public RecipeBoxRead Read()
    {
        if (service.BlockedSentence is { } blocked)
        {
            return new RecipeBoxRead.Unavailable(blocked);
        }

        HouseholdDocument document;
        try
        {
            document = service.ReadAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdFormatException)
        {
            return new RecipeBoxRead.Unavailable(HouseholdSentences.LocalDamaged);
        }

        var cards = HouseholdRecords.Read(document, RecipeMaps.Recipe);
        service.NoteUnreadable(cards.Unreadable.Count);
        var standing = service.Standing;
        var unmet = standing.State is HouseholdFileState.NotCompared or HouseholdFileState.LocalOnly or HouseholdFileState.WaitingForShare;
        if (cards.Items.Count == 0 && unmet)
        {
            return new RecipeBoxRead.NoHouseholdFile(standing.Sentence);
        }

        return new RecipeBoxRead.Ready(new RecipeBoxSnapshot(
            cards.Items,
            new Dictionary<RecipeId, CardHistory>(),
            new HashSet<RecipeId>()));
    }

    public void Move(CuisineMove move)
    {
        ArgumentNullException.ThrowIfNull(move);
        try
        {
            service.EditAsync(
                (document, stamp, writer) =>
                {
                    var result = move.Apply(document, stamp, writer);
                    return result.Accepted ? result.Document : throw new InvalidOperationException(result.Refusal);
                },
                CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (HouseholdUnreadableException exception)
        {
            throw new InvalidOperationException(exception.Message, exception);
        }
        catch (HouseholdFormatException exception)
        {
            throw new InvalidOperationException(HouseholdSentences.LocalDamaged, exception);
        }
    }

    public CookSoonResult PlaceNextWeek(RecipeId card) => CookSoonResult.NoPlannerYet;
}

/// <summary>
/// Where a checked card is kept: a <c>recipe</c> record in the household file, written through <see cref="RecipeMaps"/>. Refused with
/// the file's own sentence while the file will not open, and with nothing written.
/// </summary>
public sealed class HouseholdRecipeKeeper(HouseholdFileService service) : IRecipeKeeper
{
    public RecipeKeepResult Keep(Recipe recipe)
    {
        ArgumentNullException.ThrowIfNull(recipe);
        if (service.BlockedSentence is { } blocked)
        {
            return RecipeKeepResult.Refused(blocked);
        }

        try
        {
            service.EditAsync(
                (document, stamp, writer) => HouseholdRecords.Put(document, RecipeMaps.Recipe, recipe, stamp, writer),
                CancellationToken.None).GetAwaiter().GetResult();
            return RecipeKeepResult.Done;
        }
        catch (HouseholdUnreadableException exception)
        {
            return RecipeKeepResult.Refused(exception.Message);
        }
        catch (HouseholdFormatException)
        {
            return RecipeKeepResult.Refused(HouseholdSentences.LocalDamaged);
        }
    }
}
