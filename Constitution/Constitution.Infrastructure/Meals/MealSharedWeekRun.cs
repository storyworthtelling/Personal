using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// Everything the shared-week run reaches outside itself, named once (D127). Like <see cref="MealRecipeSeams"/> it holds no lease, history, calendar, record of written keys or place to save a
/// week; the weeks come from the household file, which it can only read (<see cref="IWeekPlanSource"/>).
/// </summary>
/// <param name="Plans">The weeks other phones published, read from this phone's copy of the household file.</param>
/// <param name="Recipes">The recipe box over the household file.</param>
/// <param name="Taste">The taste profile, for the rating page's underline.</param>
/// <param name="Documents">The tablet, the record of deliveries, the roster, the pantry and the rest of what the documents step reaches.</param>
/// <param name="Log">The Body trail's one-method seam.</param>
public sealed record MealSharedSeams(IWeekPlanSource Plans, IRecipeBoxSource Recipes, IMealTasteSource Taste, MealDocumentSeams Documents, IWorkoutWeekLog Log);

/// <summary>
/// The phone that does not hold the planning lease, sending its own adult's tablet from the week the lease holder published (D127, closing the gap D125 recorded). It reads the
/// household file's <c>week-plan</c> records, takes the ones another phone wrote, and hands each due week to the unchanged <see cref="MealDocumentsStep"/>: the sheet while it is not on the
/// tablet and the deadline is ahead (with the template pages and the denied evenings' lines the plan carries) and each of this adult's recipes in its hour. The step's own rules decide
/// everything else: the hour, the cook, the blank-page rule and the fingerprint comparison, so a page already on the tablet is left alone and nothing is sent twice.
///
/// <para><b>What it does not do.</b> It plans nothing, places nothing, takes no lease, chooses no template, saves no week and writes no record of any kind: its seams hold none of that, and a
/// source guard keeps those words out of this file. It never writes a <c>week-plan</c> record; only the planning phone's run does.</para>
///
/// <para><b>Failing closed.</b> A household file that will not open, a recipe box or a taste profile that cannot be read stop the run with a typed reason
/// (<see cref="MealRecipesStoppedBecause"/>). Nothing due is a quiet answer with no trail line, since the pass may ask on every look. Anything the run cannot name is a bug and propagates.</para>
/// </summary>
public static class MealSharedWeekRun
{
    public const string FileSentence = "The household file cannot be read, so no week from the other phone was used.";
    public const string BoxSentence = "The recipe box cannot be read now, so nothing from the other phone's week was sent.";
    public const string TasteSentence = "The taste profile is there and cannot be read, so nothing from the other phone's week was sent.";

    public static async Task<MealRecipesOutcome> RunAsync(MealWeekRequestInputs inputs, MealSharedSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(seams);
        cancellation.ThrowIfCancellationRequested();

        var outcome = await LookAsync(inputs, seams, cancellation);
        if (outcome is not MealRecipesOutcome.Nothing)
        {
            var phrase = MealRecipesTrail.LogPhrase(outcome);
            if (phrase.Length > 0)
            {
                seams.Log.Outcome(phrase);
            }
        }

        return outcome;
    }

    private static async Task<MealRecipesOutcome> LookAsync(MealWeekRequestInputs inputs, MealSharedSeams seams, CancellationToken cancellation)
    {
        var nothing = new MealRecipesOutcome.Nothing(MealRecipesTrail.NoneDueSentence);
        var read = seams.Plans.Read();
        if (read is WeekPlansRead.Blocked)
        {
            return new MealRecipesOutcome.Stopped(MealRecipesStoppedBecause.WeeksUnreadable, FileSentence);
        }

        var held = (WeekPlansRead.Held)read;
        var theirs = SharedWeeks.FromOthers(held.Set, held.Me);
        var person = MealRecipeHour.TabletPerson(inputs.Rules, inputs.DeliverFor);
        var due = SharedWeeks.Due(theirs, inputs.Now, person, seams.Documents.Delivered.Find);
        if (due.Count == 0)
        {
            return nothing;
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
        foreach (var week in due)
        {
            cancellation.ThrowIfCancellationRequested();
            var plan = theirs.First(item => item.WeekStart == week.Week);
            var documents = await MealDocumentsStep.RunAsync(
                new MealDocumentRequest(plan.Stored.Week, cards, inputs.Rules, inputs.Now, inputs.DeliverFor)
                {
                    RecipesOnly = !week.Sheet,
                    SlowCookerCards = inputs.Placement.SlowCookerCards,
                    KidsMeals = inputs.KidsMeals,
                    Ratings = ratings,
                    Daily = plan.Daily,
                    ExtraLines = plan.ExtraLines,
                },
                seams.Documents,
                cancellation);
            weeks.Add(new MealRecipesWeek(week.Week, documents));
        }

        return new MealRecipesOutcome.Done(weeks);
    }
}
