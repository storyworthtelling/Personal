using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Groceries;
using Constitution.Domain.Household;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.ReadBack;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Documents;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The second half of the weekly meal run (the meals counterpart of what <c>WorkoutWeekRun</c> does after placement): turns the kept week into the printed
/// documents, puts them on <b>this phone's</b> tablet and tells the read-back what to look for. The sheet is made once for the week; a recipe is made for each
/// cooked dinner. Every stage is a pure function or a seam that already existed; this joins them and decides what a failure at each means.
///
/// <para><b>Whose tablet.</b> Each phone is paired to its own tablet (D85), so this install sends the sheet to its tablet and only the recipes whose cook is
/// the adult that tablet belongs to. The other adult's install sends hers from the same saved week; the sheet is the same on both, and sending it twice
/// on one tablet is impossible because a send is idempotent (below). Only the first adult in <see cref="MealDocumentRequest.DeliverFor"/> is served: a phone has one tablet.</para>
///
/// <para><b>When.</b> The sheet is sent when the week is planned, while the deadline is ahead (after it silence has approved the week). A recipe goes an
/// hour before its cook block and never once the block has begun (<see cref="RecipeDelivery"/>, <see cref="MealRedeliveryRules"/>): a run earlier than that
/// reports <see cref="MealDocumentVerdict.NotYet"/> with the moment, and a later run sends it: the pass that finds a recipe in its hour (<see cref="MealDocumentRequest.RecipesOnly"/>, D120). A page already on the tablet is replaced
/// only while its slot has not begun, and only if it is still blank (<see cref="MealDocumentDelivery"/>).</para>
///
/// <para><b>Recipes only (D120).</b> A request with <see cref="MealDocumentRequest.RecipesOnly"/> skips the sheet and the pantry and decides each cooked dinner's recipe by exactly the same rules, so the
/// worker can send the week in progress its recipes in their hour without touching a sheet that is past its deadline.</para>
///
/// <para><b>Idempotent.</b> The page is judged by a fingerprint of its bytes, which a render repeats exactly. A run on a week that has not changed finds
/// every page unchanged and sends, trashes and records nothing. A page is recorded for the read-back, and as delivered, only after the tablet took it; a refused
/// page is simply not recorded, raises the existing reMarkable notice through <see cref="Constitution.Infrastructure.Delivery.DeliveryService"/> and is tried again by the next run.</para>
///
/// <para><b>One page's failure never blocks the next.</b> A page that cannot be drawn is reported for that page. The roster or the pantry being unreadable
/// stops the whole step before anything is touched, with a typed reason: an unreadable store is never read as an empty one. Anything else is a bug and propagates with its stack.</para>
/// </summary>
public static class MealDocumentsStep
{
    public const string NoTabletSentence = "This phone has no adult whose tablet it delivers to, so no meal document was sent.";
    public const string NotAnAdultSentence = "The adult this phone delivers for is not one of the household's adults, so no meal document was sent.";

    public static async Task<MealDocumentsResult> RunAsync(MealDocumentRequest request, MealDocumentSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(seams);
        cancellation.ThrowIfCancellationRequested();

        var result = await RunStepsAsync(request, seams, cancellation);
        foreach (var line in MealDocumentTrail.Lines(result))
        {
            seams.Log.Outcome(line);
        }

        return result;
    }

    private static async Task<MealDocumentsResult> RunStepsAsync(MealDocumentRequest request, MealDocumentSeams seams, CancellationToken cancellation)
    {
        if (request.DeliverFor.Count == 0)
        {
            return new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.NoTablet, NoTabletSentence);
        }

        var person = request.Rules.FindAdult(request.DeliverFor[0])?.Name;
        if (person is null)
        {
            return new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.NoTablet, NotAnAdultSentence);
        }

        var read = seams.Roster.Read();
        if (read is MealRosterRead.Unavailable unavailable)
        {
            return new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.RosterUnavailable, unavailable.Reason);
        }

        var roster = (MealRosterRead.Ready)read;

        // A recipes-only request has no sheet to build, so the pantry (the sheet's grocery page) is not needed and an unreadable one stops nothing.
        var pantry = Pantry.Empty;
        string? pantrySentence = null;
        var unreadable = 0;
        if (!request.RecipesOnly)
        {
            switch (seams.Pantry.Read())
            {
                case MealPantryRead.Ready ready:
                    pantry = ready.Pantry;
                    unreadable = ready.UnreadableRecords;
                    break;
                case MealPantryRead.NoneYet none:
                    pantrySentence = none.Sentence;
                    break;
                case MealPantryRead.Unreadable damaged:
                    return new MealDocumentsResult.Stopped(MealDocumentsStoppedBecause.PantryUnreadable, damaged.Reason);
                default:
                    throw new InvalidOperationException("The pantry came back as neither a pantry nor a reason.");
            }
        }

        var documents = new List<MealDocumentOutcome>();
        if (!request.RecipesOnly)
        {
            documents.Add(await SheetAsync(request, seams, person, roster.People.Count, pantry, cancellation));
        }

        foreach (var dinner in request.Week.Dinners.Where(item => item.Kind == DinnerKind.Cooked))
        {
            cancellation.ThrowIfCancellationRequested();
            documents.Add(await RecipeAsync(request, seams, person, roster.People, dinner, cancellation));
        }

        return new MealDocumentsResult.Done(documents, pantrySentence, unreadable);
    }

    private static async Task<MealDocumentOutcome> SheetAsync(
        MealDocumentRequest request, MealDocumentSeams seams, string person, int headcount, Pantry pantry, CancellationToken cancellation)
    {
        var day = request.Week.WeekStart;
        var name = DeliveredName.For(DeliveryKind.Meals, day);
        MealSheetModel model;
        byte[] pdf;
        ExpectedDocument expected;
        try
        {
            model = MealSheetBuilder.Build(new MealSheetBuildRequest(
                request.Week, request.Cards, request.Rules.Zone, headcount, pantry, seams.Budgets.Load(), seams.Holidays)
            {
                History = request.History,
                SlowCookerCards = request.SlowCookerCards,
                KidsMeals = request.KidsMeals,
                Daily = request.Daily,
                ExtraLines = request.ExtraLines,
            });
            pdf = ConstitutionDocumentRenderer.Render(MealSheetDocument.Build(model));
            expected = ExpectedDocument.ForMealSheet(model, person);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            return new MealDocumentOutcome(name, DeliveryKind.Meals, person, day, MealDocumentVerdict.NotBuilt, failure);
        }

        var current = new CurrentMealDocument(DocumentFingerprint.Of(pdf), model.Deadline, null);
        return await DeliverAsync(seams, DeliveryKind.Meals, day, null, person, pdf, current, expected, request.Now, cancellation);
    }

    private static async Task<MealDocumentOutcome> RecipeAsync(
        MealDocumentRequest request, MealDocumentSeams seams, string person, IReadOnlyList<RosterEntry> people, DinnerChoice dinner, CancellationToken cancellation)
    {
        var cook = dinner.Cook!;
        var name = DeliveredName.For(DeliveryKind.Recipe, dinner.Day, cook);
        RecipePage page;
        RecipeDeliveryPlan plan;
        try
        {
            var card = request.Cards.FirstOrDefault(item => item.Id == dinner.Recipe)
                ?? throw new ArgumentException($"{dinner.Day.DayOfWeek}'s dinner names a card that is not in the box, so its recipe cannot be printed.", nameof(request));
            page = RecipePagesBuilder.Build(dinner, card, people, request.Rules.Zone, request.Ratings, request.KidsMeals.GetValueOrDefault(dinner.Day));
            plan = RecipeDelivery.Plan(cook, request.Rules.Adults.Select(adult => adult.Name).ToList(), page.Model, request.Rules.Zone, request.Now);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            return new MealDocumentOutcome(name, DeliveryKind.Recipe, cook, dinner.Day, MealDocumentVerdict.NotBuilt, failure);
        }

        if (!plan.Tablets.Contains(person, StringComparer.OrdinalIgnoreCase))
        {
            return new MealDocumentOutcome(name, DeliveryKind.Recipe, cook, dinner.Day, MealDocumentVerdict.NotHere);
        }

        byte[] pdf;
        ExpectedDocument expected;
        try
        {
            pdf = ConstitutionDocumentRenderer.Render(RecipeWithRating.Build(page.Model, page.Rating));
            expected = ExpectedDocument.ForRecipe(page.Model, page.Rating, cook, dinner.Day, cook);
        }
        catch (Exception failure) when (failure is ArgumentException or InvalidOperationException)
        {
            return new MealDocumentOutcome(name, DeliveryKind.Recipe, cook, dinner.Day, MealDocumentVerdict.NotBuilt, failure);
        }

        // The cook block as the owner will see it on the calendar may begin before the first step the card's own minutes ask for (the planner holds a block
        // to 30 to 120 minutes), and the paper must be there before whichever comes first.
        var steps = RecipeDelivery.CookBlockStart(page.Model, request.Rules.Zone);
        var blockStart = dinner.CookStart!.Value < steps ? dinner.CookStart.Value : steps;
        var current = new CurrentMealDocument(DocumentFingerprint.Of(pdf), blockStart, blockStart - RecipeDelivery.Lead);
        return await DeliverAsync(seams, DeliveryKind.Recipe, dinner.Day, cook, cook, pdf, current, expected, request.Now, cancellation);
    }

    private static async Task<MealDocumentOutcome> DeliverAsync(
        MealDocumentSeams seams,
        DeliveryKind kind,
        DateOnly day,
        string? subject,
        string person,
        byte[] pdf,
        CurrentMealDocument current,
        ExpectedDocument expected,
        DateTimeOffset now,
        CancellationToken cancellation)
    {
        var name = DeliveredName.For(kind, day, subject);
        var previous = seams.Delivered.Find(name);
        var decision = MealRedeliveryRules.Decide(previous, current, now);
        switch (decision)
        {
            case MealRedelivery.LeaveAlone:
                return new MealDocumentOutcome(name, kind, person, day, MealDocumentVerdict.Unchanged);
            case MealRedelivery.TooLate:
                return new MealDocumentOutcome(name, kind, person, day, MealDocumentVerdict.TooLate);
            case MealRedelivery.NotYet:
                return new MealDocumentOutcome(name, kind, person, day, MealDocumentVerdict.NotYet, At: current.SendFrom);
        }

        var delivery = new MealDocumentDelivery(seams.Delivery, seams.Tablet, seams.InkProbe);
        var replace = decision == MealRedelivery.Replace;

        // A replacement is only ever asked for before the slot. A first send has no record to say what is on the tablet, so it looks the title up;
        // a blank page it finds is replaced only while the slot has not begun.
        var sent = replace
            ? await delivery.ReplaceAsync(kind, day, subject, pdf, cancellation)
            : await delivery.SendAsync(kind, day, subject, pdf, mayReplaceBlank: now < current.SlotStart, cancellation);
        if (sent.Kept is { } kept)
        {
            return new MealDocumentOutcome(sent.Name, kind, person, day, MealDocumentVerdict.Kept, kept.Failure, Ink: kept.Ink);
        }

        if (!sent.Result.Delivered)
        {
            return new MealDocumentOutcome(sent.Name, kind, person, day, MealDocumentVerdict.NotDelivered, sent.Result.Failure);
        }

        // The tablet has it. The read-back's record first (without it the page is never read), then ours.
        try
        {
            seams.Expected.Record(expected);
            seams.Delivered.Record(new DeliveredMealDocument(sent.Name, kind, person, day, current.SlotStart, current.Fingerprint));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            return new MealDocumentOutcome(sent.Name, kind, person, day, MealDocumentVerdict.NotDelivered, failure);
        }

        return new MealDocumentOutcome(sent.Name, kind, person, day, replace ? MealDocumentVerdict.Replaced : MealDocumentVerdict.Delivered);
    }
}
