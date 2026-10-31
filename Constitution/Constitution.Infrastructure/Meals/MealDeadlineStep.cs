using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts.Screens;
using Constitution.Domain.Workouts.Pipeline;
using Constitution.Infrastructure.Household;

namespace Constitution.Infrastructure.Meals;

/// <summary>Everything the deadline step reaches outside itself, named once. None holds a token, a key or a passphrase.</summary>
/// <param name="Lease">Whether this install holds the planning lease (<see cref="HouseholdPlanningStanding.AskAsync"/>).</param>
/// <param name="Weeks">The planned weeks the meal run kept.</param>
/// <param name="Decisions">The weeks already decided.</param>
/// <param name="Answers">The two adults' meal answers, from the household file.</param>
/// <param name="Library">The rotating templates' library.</param>
/// <param name="Notes">The taste notes the household file keeps.</param>
/// <param name="Judge">Decides whether written words are a lasting lesson; <see cref="UnavailableTasteJudge"/> until a real one is built.</param>
/// <param name="Log">The Body trail's one-method seam.</param>
/// <param name="Calendar">The calendar side that takes a denied evening's cook block off (<see cref="DeniedEveningCalendar"/>); null means none was handed over, and the removal is a typed "not removed" tried again next run.</param>
public sealed record MealDeadlineSeams(
    Func<CancellationToken, Task<PlanningStanding>> Lease,
    IMealWeekRecords Weeks,
    IMealDecisionRecords Decisions,
    IMealAnswerSource Answers,
    ITemplateLibraryStore Library,
    IMealTasteNotes Notes,
    ITasteJudge Judge,
    IWorkoutWeekLog Log,
    MealDeadlineCalendar? Calendar = null);

/// <summary>
/// What happens on the planner's phone once the meal sheet's deadline (Sunday 8:00 on the owner's clock, D104) has passed. The meal run built and sent the
/// sheet; both adults' tablets were read and their answers written to the household file; this step reads them all and settles the week once.
///
/// <para><b>Who and which week.</b> Only the install holding the planning lease decides; the partner's phone gets a typed "not the planner". The week is the newest
/// kept week whose deadline has passed and that the record of decided weeks does not hold. A decided week is never decided twice, so a run that finds nothing new says so.
/// An older week left undecided is not decided late: its evenings are over.</para>
///
/// <para><b>What it does.</b> It reads every answer of the week (<see cref="IMealAnswerSource"/>) and the sheet's own lines
/// (<see cref="MealDeadlineRules.ExpectedLines"/>) and asks <see cref="MealDeadline.Decide"/>: a tick that stands or silence approves, a denial the moment it was read
/// on time wins, a late answer reverses nothing, and a line no tablet was read for is approved and counted (D103), in the trail only. A denied dinner's written words
/// go to the taste judge as evidence naming the card its weekday has (<see cref="MealDeadlineRules.DinnerEvidence"/>); nothing is inferred from the words here, so a
/// denial is not a "never again". Pages 2 and 3 go through <see cref="TemplateFeedback"/>: the library is updated, a tick teaches one like per ticker, silence
/// approves and teaches nothing, a written no rejects and its words go to the judge too.</para>
///
/// <para><b>The judge.</b> Whether words are a lasting lesson is the <see cref="ITasteJudge"/>'s call. With none (<see cref="UnavailableTasteJudge"/>) every piece is
/// unjudged; the week is recorded as having evidence pending, and the next run asks again (only the judging: the library and the likes are never repeated).</para>
///
/// <para><b>The order, and why.</b> Read everything, then judge (it changes nothing), then save the notes, then the library, then the record of the decision last.
/// A stop at any save leaves the week undecided, and the next run decides it again with nothing lost: the notes save is idempotent, and the library is only saved once
/// its notes are safe. The record of a decided week is the one thing that makes the week final.</para>
///
/// <para><b>Fail closed.</b> A decision record, saved weeks, template library or household file that is there and cannot be read stops the step with its own typed
/// reason, and so does an answer record for the week this build cannot read: read as silence, a refused dinner would be approved. A seam's exception propagates.</para>
///
/// <para><b>A denied dinner (D111).</b> There is no replan and no revised sheet. For each denied dinner whose evening is today or ahead, the evening is left unplanned
/// in the kept week ("Not approved.", every other evening untouched), its cook block is taken off the calendar when it is still where placement wrote it
/// (<see cref="DeniedEveningCalendar"/>: an owner-moved event is left alone, silently), and the decision record names the evening
/// (<see cref="MealDecisionEntry.DeniedEvenings"/>) so the shopping list carries one plain line, "Tuesday dinner" (<see cref="MealDecisionEntry.ExtraLines"/>). A removal
/// that could not be made is recorded and tried again by later runs until the evening has passed; it never stops the decision.</para>
/// </summary>
public static class MealDeadlineStep
{
    public static async Task<MealDeadlineOutcome> RunAsync(MealDeadlineInputs inputs, MealDeadlineSeams seams, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(seams);
        cancellation.ThrowIfCancellationRequested();

        var outcome = await RunStepsAsync(inputs, seams, cancellation);
        seams.Log.Outcome(MealDeadlineTrail.LogPhrase(outcome));
        return outcome;
    }

    private static async Task<MealDeadlineOutcome> RunStepsAsync(MealDeadlineInputs inputs, MealDeadlineSeams seams, CancellationToken cancellation)
    {
        var standing = await seams.Lease(cancellation);
        if (!standing.MayPlan)
        {
            return new MealDeadlineOutcome.NotThePlanner(standing.Sentence);
        }

        MealDecisionRecord decided;
        switch (seams.Decisions.Load())
        {
            case MealDecisionRecordRead.NothingYet:
                decided = MealDecisionRecord.Empty;
                break;
            case MealDecisionRecordRead.Held held:
                decided = held.Record;
                break;
            case MealDecisionRecordRead.Unreadable:
                return Stop(MealDeadlineStoppedBecause.DecisionsUnreadable, "The saved meal decisions cannot be read, so no week was decided.");
            default:
                throw new InvalidOperationException("The decided weeks came back as neither a record nor a reason.");
        }

        MealWeekRecord kept;
        switch (seams.Weeks.Load())
        {
            case MealWeekRecordRead.NothingYet:
                return Nothing(MealDeadlineNothingBecause.NoWeekKept, "No planned week is kept, so there is no sheet to decide.");
            case MealWeekRecordRead.Held held:
                kept = held.Record;
                break;
            case MealWeekRecordRead.Unreadable:
                return Stop(MealDeadlineStoppedBecause.WeeksUnreadable, "The saved meal weeks cannot be read, so no week was decided.");
            default:
                throw new InvalidOperationException("The saved weeks came back as neither a record nor a reason.");
        }

        var zone = inputs.Rules.Zone;
        var choice = MealDeadlineTarget.Choose(kept, decided, inputs.Now, zone);
        if (choice is MealDeadlineChoice.Nothing nothing)
        {
            return Nothing(nothing.Because, nothing.Sentence);
        }

        var target = ((MealDeadlineChoice.Decide)choice).Target;

        var existing = decided.For(target.WeekStart);
        var week = target.WeekStart;

        if (existing is { EvidencePending: false })
        {
            return await RetryRemovalsAsync(inputs, seams, decided, existing, cancellation);
        }

        IReadOnlyList<MealAnswer> answers;
        switch (seams.Answers.Read(week))
        {
            case MealAnswersRead.Held { UnreadableRecords: 0 } held:
                answers = held.Answers;
                break;
            case MealAnswersRead.Held:
                return Stop(MealDeadlineStoppedBecause.AnswersUnreadable, "An answer for this week cannot be read by this version, so the week was not decided.");
            case MealAnswersRead.Unreadable:
                return Stop(MealDeadlineStoppedBecause.AnswersUnreadable, "The household file cannot be read, so the week was not decided.");
            default:
                throw new InvalidOperationException("The answers came back as neither answers nor a reason.");
        }

        TemplateLibrary? library;
        switch (seams.Library.Load())
        {
            case TemplateLibraryRead.Held held:
                library = held.Library;
                break;
            case TemplateLibraryRead.NoneYet:
                library = null;
                break;
            case TemplateLibraryRead.Unreadable:
                return Stop(MealDeadlineStoppedBecause.LibraryUnreadable, "The template library cannot be read, so the week was not decided.");
            default:
                throw new InvalidOperationException("The template library came back as neither a library nor a reason.");
        }

        var today = ZoneClock.DayOf(inputs.Now, zone);
        var retry = existing is not null;
        var decision = MealDeadline.Decide(answers, MealDeadline.ForWeek(week, zone), inputs.Now, MealDeadlineRules.ExpectedLines(target, library));

        // A retry reads the template words again from the answers (the library already took them and must not be changed), so the library is shown with the week's
        // feedback undone; its result is used only for the evidence.
        var feedback = library is null
            ? new TemplateFeedbackResult(TemplateLibrary.Empty, [], [])
            : TemplateFeedback.From(decision, answers, retry ? Undone(library, week) : library, today, week);
        var likes = retry ? [] : feedback.Likes;
        var fed = library is null || retry ? 0 : feedback.Library.Templates.Count(template => template.FeedbackWeek == week && library.Find(template.Id)?.FeedbackWeek != week);

        var evidence = MealDeadlineRules.DinnerEvidence(decision, target, today, existing?.DeniedDishes).Concat(feedback.Evidence).ToList();
        var learned = await TasteLearner.LearnAsync(evidence, seams.Judge, cancellation);

        var notes = likes.Concat(learned.Notes).ToList();
        if (notes.Count > 0 && seams.Notes.Save(notes) is MealNotesSave.Refused)
        {
            return Stop(MealDeadlineStoppedBecause.NotesNotSaved, "The taste notes could not be saved, so the week was not decided.");
        }

        if (library is not null && !retry && !TemplateRunRules.Same(library, feedback.Library) && seams.Library.Save(feedback.Library) is TemplateSave.Refused)
        {
            return Stop(MealDeadlineStoppedBecause.LibraryNotSaved, "The template library could not be saved, so the week was not decided.");
        }

        var unjudged = learned.Outcomes.Count(outcome => outcome == TasteOutcome.Unjudged);
        MealDecisionEntry entry;
        IReadOnlyList<EveningRemoval> removals;
        if (existing is null)
        {
            var ahead = MealDeadlineRules.DeniedEveningsAhead(decision, target, today);
            var cooked = ahead.Where(dinner => dinner.Kind == DinnerKind.Cooked).ToList();
            var dishes = cooked
                .Select(dinner => new DeniedDish(DeniedEveningWords.Of(dinner.Day.DayOfWeek), dinner.Recipe!.Value, dinner.CookStart!.Value, dinner.CookEnd!.Value))
                .ToList();
            var found = new List<EveningRemoval>();
            for (var index = 0; index < dishes.Count; index++)
            {
                found.Add(new EveningRemoval(
                    dishes[index].Evening,
                    await DeniedEveningCalendar.RemoveAsync(seams.Calendar, week, dishes[index], [cooked[index].Cook!], true, cancellation)));
            }

            removals = found;

            // The evening is marked unplanned just before the decision is saved: the cook and the card are only in the kept week until then.
            if (ahead.Count > 0)
            {
                var marked = MealDeadlineRules.LeaveUnplanned(target, ahead.Select(dinner => dinner.Day).ToList());
                seams.Weeks.Save(kept.With(marked));
            }

            entry = new MealDecisionEntry(week, inputs.Now, decision.ApprovedItemIds.Count, decision.Denied.Count, decision.ApprovedUnread.Count, unjudged > 0)
            {
                DeniedEvenings = ahead.Select(dinner => DeniedEveningWords.Of(dinner.Day.DayOfWeek)).ToList(),
                DeniedDishes = dishes,
                EventsPending = removals.Where(removal => removal.NotRemoved).Select(removal => removal.Evening).ToList(),
            };
        }
        else
        {
            removals = await RetryAsync(inputs, seams, existing, cancellation);
            entry = existing with { EvidencePending = unjudged > 0, EventsPending = StillPending(existing, removals) };
        }

        seams.Decisions.Save(decided.With(entry));

        return new MealDeadlineOutcome.Decided(new MealDeadlineReport(
            week,
            decision.ApprovedItemIds.Count,
            decision.Denied.Count,
            decision.ApprovedUnread.Count,
            decision.Items.Sum(item => item.Ignored.Count),
            fed,
            likes.Count,
            learned.Outcomes.Count(outcome => outcome == TasteOutcome.Lasting),
            learned.Outcomes.Count(outcome => outcome == TasteOutcome.OneOff),
            unjudged,
            entry.DeniedEvenings,
            removals,
            retry));
    }

    // The week was already decided and only a cook block waits to come off the calendar: no answer is read and no word is judged again.
    private static async Task<MealDeadlineOutcome> RetryRemovalsAsync(
        MealDeadlineInputs inputs, MealDeadlineSeams seams, MealDecisionRecord decided, MealDecisionEntry existing, CancellationToken cancellation)
    {
        var removals = await RetryAsync(inputs, seams, existing, cancellation);
        var entry = existing with { EventsPending = StillPending(existing, removals) };
        seams.Decisions.Save(decided.With(entry));
        return new MealDeadlineOutcome.Decided(new MealDeadlineReport(existing.Week, existing.Approved, existing.Denied, existing.ApprovedUnread, 0, 0, 0, 0, 0, 0, existing.DeniedEvenings, removals, true));
    }

    private static async Task<IReadOnlyList<EveningRemoval>> RetryAsync(MealDeadlineInputs inputs, MealDeadlineSeams seams, MealDecisionEntry existing, CancellationToken cancellation)
    {
        var today = ZoneClock.DayOf(inputs.Now, inputs.Rules.Zone);
        var adults = inputs.Rules.Adults.Select(adult => adult.Name).ToList();
        var removals = new List<EveningRemoval>();
        foreach (var word in existing.EventsPending)
        {
            if (!DeniedEveningWords.TryDay(word, out var weekday) || existing.Week.AddDays((int)weekday) < today)
            {
                continue;
            }

            if (existing.DeniedDishes.FirstOrDefault(item => item.Evening == word) is not { } dish)
            {
                continue;
            }

            removals.Add(new EveningRemoval(word, await DeniedEveningCalendar.RemoveAsync(seams.Calendar, existing.Week, dish, adults, false, cancellation)));
        }

        return removals;
    }

    // What is still waiting: what this run could not remove. An evening that has passed, or one this run did not try, is let go.
    private static IReadOnlyList<string> StillPending(MealDecisionEntry existing, IReadOnlyList<EveningRemoval> removals) =>
        existing.EventsPending.Where(word => removals.Any(removal => removal.Evening == word && removal.NotRemoved)).ToList();

    private static TemplateLibrary Undone(TemplateLibrary library, DateOnly week) =>
        new(library.Templates.Select(template => template.FeedbackWeek == week ? template with { FeedbackWeek = null } : template).ToList(), library.Prints);

    private static MealDeadlineOutcome Nothing(MealDeadlineNothingBecause because, string sentence) => new MealDeadlineOutcome.NothingToDecide(because, sentence);

    private static MealDeadlineOutcome Stop(MealDeadlineStoppedBecause because, string sentence) => new MealDeadlineOutcome.Stopped(because, sentence);
}
