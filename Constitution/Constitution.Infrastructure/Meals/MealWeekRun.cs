using Constitution.Domain.Calendar;
using Constitution.Domain.Groceries;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Placement;
using Constitution.Domain.Recipes;
using Constitution.Domain.Recipes.Screens;
using Constitution.Infrastructure.Workouts;

namespace Constitution.Infrastructure.Meals;

/// <summary>
/// The weekly meal run, part one (the meals counterpart of <c>WorkoutWeekRun</c>): asks whether this phone plans the week, reads what
/// the planner needs, plans the dinners, puts the cook blocks and the Groceries trip on the calendar and keeps the week for Today's
/// dinner card and the Meals tab. Every stage is a pure function or a seam that already existed; this joins them in order and decides
/// what a failure at each seam means.
///
/// <para><b>The order.</b> The lease first: only the holder of the week plans it, and the partner's phone gets a typed "not the planner
/// this week", which is not an error. Then the recipes, the cooked history, the saved weeks, the taste profile and the calendars. Then
/// <see cref="MealWithFallback"/> (rules only until an author exists). Then <see cref="MealPlacement"/> and the Groceries trip through
/// the one <see cref="PlacementRunner"/>, on the same record of written keys and of issued numbers as the workout run. Then the week is kept.</para>
///
/// <para><b>Failing closed.</b> A recipe box, a history or a taste profile that is there and cannot be read stops the run with its own
/// typed reason: read as empty, each would plan on a memory it lost. A calendar that cannot be read for the whole week is the planner's own stop.
/// A stop never replaces the last good week that was saved. A calendar write that is refused, or a record of keys that cannot be read, is
/// reported in the placement (<see cref="MealWeekReport.Placement"/>) and plans nothing on a guess.</para>
///
/// <para><b>Idempotent.</b> The week is planned again on every run that finds this phone the planner, and a week that has not changed
/// finds every event held and every saved word the same: nothing is written to the calendar, to the record of keys or to the saved
/// weeks. The seed is the week's own number, so the same week breaks its ties the same way on every run. An evening that is over keeps the
/// dinner the saved week gave it (<see cref="MealWeekKeeping"/>).</para>
///
/// <para><b>What is not recorded as cooked.</b> The cooked history (<see cref="IMealHistoryStore"/>) is written only when a dinner is
/// confirmed cooked, which the read-back will settle later; this run never writes it. To keep the planner from serving the same dish two weeks
/// running it is shown the dinners of the weeks it planned before this one (<see cref="PlannedHistory"/>), as a stand-in that is never stored as fact.</para>
///
/// <para>Anything the run cannot name is a bug and propagates with its stack.</para>
/// </summary>
public static class MealWeekRun
{
    public static async Task<MealWeekOutcome> RunAsync(MealWeekRequestInputs inputs, MealWeekSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(seams);
        PlanningWeek.RequireStart(inputs.WeekStart, nameof(inputs));
        cancellation.ThrowIfCancellationRequested();

        var standing = await seams.Lease(cancellation);
        if (!standing.MayPlan)
        {
            return Stop(seams, MealNotPlannedBecause.NotThePlanner, standing.Sentence);
        }

        var rules = inputs.Rules;
        if (rules.Adults.Count == 0)
        {
            return Stop(seams, MealNotPlannedBecause.NoOneToPlan, "The rules name no adult to plan for.");
        }

        IReadOnlyList<Recipe> cards;
        switch (seams.Recipes.Read())
        {
            case RecipeBoxRead.Ready ready:
                cards = ready.Snapshot.Cards;
                break;
            case RecipeBoxRead.NoHouseholdFile none:
                return Stop(seams, MealNotPlannedBecause.RecipesUnavailable, none.Sentence);
            case RecipeBoxRead.Unavailable unavailable:
                return Stop(seams, MealNotPlannedBecause.RecipesUnavailable, unavailable.Reason);
            default:
                throw new InvalidOperationException("The recipe box came back as neither cards nor a reason.");
        }

        var history = seams.History.Load();
        if (history.UnreadableBecause is { } unusable)
        {
            return Stop(seams, MealNotPlannedBecause.HistoryUnreadable, unusable);
        }

        // A damaged copy of the saved weeks does not stop the run (saving sets it aside and begins again); the planner just has no earlier weeks to look back at.
        var saved = seams.Records.Load();
        var record = (saved as MealWeekRecordRead.Held)?.Record;
        var earlierUnreadable = saved is MealWeekRecordRead.Unreadable;

        var taste = seams.Taste.Read();
        if (taste is MealTasteRead.Unreadable damaged)
        {
            return Stop(seams, MealNotPlannedBecause.TasteUnreadable, damaged.Reason);
        }

        var zone = rules.Zone;
        var first = inputs.WeekStart;
        var last = first.AddDays(MealWeek.Evenings - 1);
        var reach = first.AddDays(-inputs.Groceries.DaysBefore);
        var calendars = await seams.Calendars.ReadAsync(ZoneClock.StartOfDay(reach, zone), ZoneClock.StartOfDay(first.AddDays(MealWeek.Evenings), zone), cancellation);

        // Plain calendars, not the trip-aware ones: a work trip frees the workout plan to go on, and a person on one is not home for dinner.
        var busy = rules.Adults.Select(adult => BusyTime.For(adult.Name, reach, last, rules, calendars)).ToList();

        var request = new MealWeekRequest(
            first,
            cards,
            rules,
            busy,
            inputs.ChildrenHome,
            inputs.CookThisSoon,
            PlannedHistory.Merge(history.Meals, record, first),
            null,
            null,
            inputs.Seed ?? PlanningWeek.IndexOf(first),
            inputs.Now);
        request = TasteForTheWeek.Apply(taste, request);

        var planned = await seams.Planner.PlanAsync(request, cancellation);
        var week = planned.Week;
        if (week.Stop is { } stopped)
        {
            return Stop(
                seams,
                stopped == MealStop.BoxTooSmall ? MealNotPlannedBecause.BoxTooSmall : MealNotPlannedBecause.CalendarsUnreadable,
                week.StopReason!);
        }

        var events = MealPlacement.Requests(week, rules, cards, inputs.Placement).ToList();
        var trip = GroceryPlacement.Request(first, rules, busy, inputs.ApprovedAt ?? inputs.Now, inputs.Groceries);
        if (trip is not null)
        {
            events.Add(trip);
        }

        PlacementRun? placement = null;
        if (events.Count > 0)
        {
            var runner = new PlacementRunner(new PastWriteGuard(seams.CalendarWriter, () => inputs.Now), seams.WrittenKeys, seams.KeyAssignments);
            placement = await runner.RunAsync(new PlacementRequest(first, busy, events, null), cancellation);
        }

        var dishes = week.Dinners
            .Where(dinner => dinner.Kind == DinnerKind.Cooked)
            .Select(dinner => dinner.Recipe!.Value)
            .Distinct()
            .Select(id => new StoredDish(id, cards.First(card => card.Id == id).Title))
            .ToList();
        var today = ZoneClock.DayOf(inputs.Now, zone);
        var before = record?.For(first);
        var kept = MealWeekKeeping.Keep(before, new StoredMealWeek(week, inputs.Now, dishes), today);
        var save = before is not null && MealWeekRecordCodec.ContentKey(before) == MealWeekRecordCodec.ContentKey(kept)
            ? MealRecordSave.Unchanged
            : MealRecordSave.Saved;
        if (save == MealRecordSave.Saved)
        {
            seams.Records.Save((record ?? MealWeekRecord.Empty).With(kept));
        }

        return Finish(seams, new MealWeekOutcome.Planned(new MealWeekReport(
            kept.Week, planned.Origin, planned.Reason, events, placement, trip is not null, save, earlierUnreadable)));
    }

    private static MealWeekOutcome Stop(MealWeekSeams seams, MealNotPlannedBecause because, string sentence) =>
        Finish(seams, new MealWeekOutcome.NotPlanned(because, sentence));

    private static MealWeekOutcome Finish(MealWeekSeams seams, MealWeekOutcome outcome)
    {
        seams.Log.Outcome(MealWeekTrail.LogPhrase(outcome));
        return outcome;
    }
}
