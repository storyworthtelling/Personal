using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Everything the recipes run reaches outside itself, named once (D120). It is a <see cref="MealWeekSeams"/> with the planning side taken away: there is no lease, no history,
/// no calendar and no record of written keys to reach, and the kept weeks can only be read. The documents side is the weekly run's own <see cref="MealDocumentSeams"/>.
/// </summary>
/// <param name="Weeks">The kept weeks, read only by its type (<see cref="IKeptWeeks"/> has no save).</param>
/// <param name="Recipes">The recipe box over the household file.</param>
/// <param name="Taste">The taste profile, for the rating page's underline.</param>
/// <param name="Documents">The tablet, the record of deliveries, the roster and the rest of what the documents step reaches.</param>
/// <param name="Log">The Body trail's one-method seam.</param>
public sealed record MealRecipeSeams(IKeptWeeks Weeks, IRecipeBoxSource Recipes, IMealTasteSource Taste, MealDocumentSeams Documents, IWorkoutWeekLog Log);

/// <summary>
/// The documents-only entry for the week in progress (D120, closing D116). The weekly run plans the week <i>after</i> the one that holds now, so a recipe of the week in
/// progress is never inside its hour on any run that plans; this entry takes the week already kept, finds each cooked dinner of this phone's adult whose recipe is inside its hour
/// and is not yet on the tablet (<see cref="MealRecipeHour"/>, the rule the pass asks too), and hands that kept week to <see cref="MealDocumentsStep"/> with
/// <see cref="MealDocumentRequest.RecipesOnly"/> set.
///
/// <para><b>What it does not do.</b> It does not plan, place an event, read a calendar, take the lease, choose templates, or save or change a kept week: its seams hold none of that
/// (<see cref="MealRecipeSeams"/>). It sends no sheet: the sheet's deadline is Sunday morning and the weekly run owns it. It sends no page the step would not send: the hour, the cook,
/// the blank-page rule and the fingerprint comparison are the step's, and a recipe already on the tablet is left alone.</para>
///
/// <para><b>Failing closed.</b> Kept weeks, a recipe box or a taste profile that is there and cannot be read stop the run with a typed reason (<see cref="MealRecipesStoppedBecause"/>):
/// read as empty, the taste profile would print a rating page that says nobody has rated anything, and the weeks would send nothing. A tablet that will not take a page is the step's own
/// <see cref="MealDocumentVerdict.NotDelivered"/>, not a throw here, and the next pass inside the hour tries again. Anything the run cannot name is a bug and propagates with its stack.</para>
/// </summary>
public static class MealRecipeHourRun
{
    public const string WeeksSentence = "The kept weeks are there and cannot be read, so no recipe was sent.";
    public const string BoxSentence = "The recipe box cannot be read now, so no recipe was sent.";
    public const string TasteSentence = "The taste profile is there and cannot be read, so no recipe was sent.";

    public static async Task<MealRecipesOutcome> RunAsync(MealWeekRequestInputs inputs, MealRecipeSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(seams);
        cancellation.ThrowIfCancellationRequested();

        var outcome = await LookAsync(inputs, seams, cancellation);
        var phrase = MealRecipesTrail.LogPhrase(outcome);
        if (phrase.Length > 0)
        {
            seams.Log.Outcome(phrase);
        }

        return outcome;
    }

    private static async Task<MealRecipesOutcome> LookAsync(MealWeekRequestInputs inputs, MealRecipeSeams seams, CancellationToken cancellation)
    {
        var read = seams.Weeks.Load();
        MealWeekRecord kept;
        switch (read)
        {
            case MealWeekRecordRead.NothingYet:
                return new MealRecipesOutcome.Nothing(MealRecipesTrail.NoneDueSentence);
            case MealWeekRecordRead.Unreadable:
                return new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.WeeksUnreadable, WeeksSentence);
            case MealWeekRecordRead.Held held:
                kept = held.Record;
                break;
            default:
                throw new InvalidOperationException("The saved weeks came back as neither a record nor a reason.");
        }

        var person = MealRecipeHour.TabletPerson(inputs.Rules, inputs.DeliverFor);
        var due = MealRecipeHour.DueWeeks(read, inputs.Now, person, seams.Documents.Delivered.Find);
        if (due.Count == 0)
        {
            return new MealRecipesOutcome.Nothing(MealRecipesTrail.NoneDueSentence);
        }

        IReadOnlyList<Recipe> cards;
        switch (seams.Recipes.Read())
        {
            case RecipeBoxRead.Ready ready:
                cards = ready.Snapshot.Cards;
                break;
            case RecipeBoxRead.NoHouseholdFile:
            case RecipeBoxRead.Unavailable:
                return new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.RecipesUnavailable, BoxSentence);
            default:
                throw new InvalidOperationException("The recipe box came back as neither cards nor a reason.");
        }

        var taste = seams.Taste.Read();
        if (taste is MealTasteRead.Unreadable)
        {
            return new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.TasteUnreadable, TasteSentence);
        }

        var ratings = TasteForTheWeek.LastRatings(taste);
        var weeks = new List<MealRecipesWeek>();
        foreach (var weekStart in due)
        {
            cancellation.ThrowIfCancellationRequested();
            var week = kept.For(weekStart) ?? throw new InvalidOperationException("A week reported as due is not in the record it was found in.");
            var documents = await MealDocumentsStep.RunAsync(
                new MealDocumentRequest(week.Week, cards, inputs.Rules, inputs.Now, inputs.DeliverFor)
                {
                    RecipesOnly = true,
                    SlowCookerCards = inputs.Placement.SlowCookerCards,
                    KidsMeals = inputs.KidsMeals,
                    Ratings = ratings,
                },
                seams.Documents,
                cancellation);
            weeks.Add(new MealRecipesWeek(weekStart, documents));
        }

        return new MealRecipesOutcome.Done(weeks);
    }
}
