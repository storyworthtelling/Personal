using Constitution.Domain.Delivery;
using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.Meals;
using Constitution.Domain.Workouts;

namespace Constitution.Domain.ReadBack;

/// <summary>
/// A document that went to the tablet and whose ink is still to be read: its name on the tablet, whose it
/// is, and its pages with their boxes and rules. The name is <see cref="DeliveredName"/>'s, so the title
/// the read-back looks for is the title delivery uploaded under.
///
/// <para>A workout is a session: <see cref="Marks"/> names the ids its pages are answered under so the day
/// can be closed (<see cref="DayClose"/>). The same ids ("done", "feel") appear on every exercise page, which is
/// harmless for that purpose: any page's tick settles the day, and no page's silence can hide another's.</para>
/// </summary>
public sealed partial class ExpectedDocument
{
    public ExpectedDocument(DeliveryKind kind, DateOnly day, string? subject, string person, SessionKind? session, IReadOnlyList<ExpectedPage> pages, IReadOnlyList<TasteRatingRow>? ratingRows = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        RatingRows = (ratingRows ?? []).ToList();
        if (RatingRows.Count > 0 && kind != DeliveryKind.Recipe)
        {
            throw new ArgumentException("Only a recipe's rating page has rating rows to carry.", nameof(ratingRows));
        }

        if (RatingRows.Select(row => row.Group).Distinct(StringComparer.Ordinal).Count() != RatingRows.Count)
        {
            throw new ArgumentException("Two rating rows cannot share a circle group; the reply could not say which person was meant.", nameof(ratingRows));
        }
        if (string.IsNullOrWhiteSpace(person))
        {
            throw new ArgumentException("A document belongs to someone; the read-back records its answers under them.", nameof(person));
        }

        Kind = kind;
        Day = day;
        Subject = string.IsNullOrWhiteSpace(subject) ? null : subject.Trim();
        Person = person.Trim();
        Session = session;
        Pages = pages.ToList();

        if (Pages.Select(page => page.Number).Distinct().Count() != Pages.Count)
        {
            throw new ArgumentException("Two pages of one document cannot share a number; the reply could not say which was meant.", nameof(pages));
        }

        switch (kind)
        {
            case DeliveryKind.Digest:
                // Owner decision amending C-2.2.3: the digest is read for one thing only, the planner's four ruled lines on its last page.
                if (session is not null || Pages.Count > 1 || Pages.Any(page => !IsPlannerNotesPage(page)))
                {
                    throw new ArgumentException(
                        "The digest holds no session and, at most, the one page of planner notes (only the four note lines, nothing about the body).", nameof(pages));
                }

                break;
            case DeliveryKind.Workout:
                if (session is null)
                {
                    throw new ArgumentException("A workout is a session of some kind; the ledger records which.", nameof(session));
                }

                Marks = new SessionMarks(Person, day, session.Value, SessionTargetIds(Pages));
                if (Marks.TargetIds.Count == 0)
                {
                    throw new ArgumentException("A workout with no box to tick cannot be read as done or skipped.", nameof(pages));
                }

                break;
            case DeliveryKind.Meals:
                if (session is not null || Pages.Count == 0)
                {
                    throw new ArgumentException("A meals document has pages to read and no session.", nameof(pages));
                }

                break;
            case DeliveryKind.Recipe:
                if (session is not null || Pages.Count == 0)
                {
                    throw new ArgumentException("A recipe has its rating page to read and no session.", nameof(pages));
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, "This kind of document has no read-back rule.");
        }
    }

    public DeliveryKind Kind { get; }

    public DateOnly Day { get; }

    public string? Subject { get; }

    public string Person { get; }

    public SessionKind? Session { get; }

    public IReadOnlyList<ExpectedPage> Pages { get; }

    /// <summary>
    /// A recipe's rating rows as the page was built: for each circle group, the person and the card it rates. Carried so the
    /// hook that records a rating names them by id and does not look them up again by printed name and title, which a renamed
    /// person, a kids' dish with its own card title or two cards of one title would make ambiguous. Empty for every other kind of
    /// document, and for a recipe stored before the rows were carried (the hook then falls back to the words).
    /// </summary>
    public IReadOnlyList<TasteRatingRow> RatingRows { get; }

    /// <summary>The title delivery uploaded it under, which is the title it is found by.</summary>
    public string Name => DeliveredName.For(Kind, Day, Subject);

    /// <summary>False for the digest, which is only ever trashed.</summary>
    public bool ReadsInk => Kind != DeliveryKind.Digest && Pages.Count > 0;

    /// <summary>True for a digest that carries its planner-notes page, read once at Sunday's run (owner decision amending C-2.2.3).</summary>
    public bool ReadsPlannerNotes => Kind == DeliveryKind.Digest && Pages.Count > 0;

    private static bool IsPlannerNotesPage(ExpectedPage page) =>
        page.Rules.Count == DigestIds.PlannerNotes.Count
        && page.Rules.All(rule => rule is StapleRule staple && DigestIds.PlannerNotes.Contains(staple.LineId))
        && page.Rules.Cast<StapleRule>().Select(rule => rule.LineId).Distinct().Count() == DigestIds.PlannerNotes.Count;

    /// <summary>
    /// The digest as the read-back holds it: delivered on <paramref name="saturday"/>, with its last page's four ruled lines to be read at Sunday's run, and nothing else on it read
    /// (no Body value is ever a target). The page is the one <c>DigestMeals.NextWeekPage</c> draws, so the ids are the painter's own.
    /// </summary>
    public static ExpectedDocument ForDigest(InkPageLayout notesPage, DateOnly saturday, string person)
    {
        ArgumentNullException.ThrowIfNull(notesPage);
        var rules = DigestIds.PlannerNotes
            .Select((id, index) => (InkRule)new StapleRule($"Note {index + 1}", id))
            .ToList();
        return new ExpectedDocument(DeliveryKind.Digest, saturday, null, person, null, [new ExpectedPage(5, new InkReadout(notesPage.Map), rules)]);
    }

    /// <summary>The session as the ledger sees it; null for a document that is not a session.</summary>
    public SessionMarks? Marks { get; }

    /// <summary>
    /// The rules of a workout as <see cref="WorkoutDocument"/> draws it: the Done tick and the easy / right / hard
    /// choice on every exercise page, and on an injury day the "Still sore?" tick on every page, the cover included.
    /// </summary>
    public static ExpectedDocument ForWorkout(WorkoutDocumentModel model, string person, SessionKind session, DateOnly day, string? subject = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layouts = WorkoutDocument.Build(model);
        var pages = new List<ExpectedPage>();
        for (var index = 0; index < layouts.Count; index++)
        {
            var map = layouts[index].Map;
            var rules = new List<InkRule>();
            if (model.Soreness is { } soreness && map.TryGet(WorkoutIds.StillSore) is not null)
            {
                rules.Add(new StillSoreRule(soreness.Place, WorkoutIds.StillSore));
            }

            // Page 1 is the cover; exercise n is page n + 1.
            if (index >= 1 && map.TryGet(WorkoutIds.Done) is not null)
            {
                rules.Add(new ExercisePageRule(model.Exercises[index - 1].Name, WorkoutIds.Done, WorkoutIds.Rating));
            }

            if (rules.Count > 0)
            {
                pages.Add(new ExpectedPage(index + 1, new InkReadout(map), rules));
            }
        }

        return new ExpectedDocument(DeliveryKind.Workout, day, subject, person, session, pages);
    }

    /// <summary>
    /// The rules of the dinners page as <see cref="MealSheetDocument"/> draws it: one <see cref="MealItemRule"/> per evening in the sheet's own order (the planning week's, Sunday to Saturday),
    /// tick or written reason, approved by silence at the model's deadline. Pages 2 and 3 add one rule per breakfast, lunch and lunchbox line when the model carries them; later pages add their own.
    /// </summary>
    public static ExpectedDocument ForMealSheet(MealSheetModel model, string person)
    {
        ArgumentNullException.ThrowIfNull(model);

        var layouts = MealSheetDocument.Build(model);
        var map = layouts[0].Map;
        var rules = new List<InkRule>();
        foreach (var dinner in model.Week.Dinners)
        {
            var weekday = dinner.Day.DayOfWeek;
            var yes = MealSheetIds.DinnerYes(weekday);
            var reason = MealSheetIds.DinnerReason(weekday);
            if (map.TryGet(yes) is not null && map.TryGet(reason) is not null)
            {
                rules.Add(new MealItemRule($"{weekday}'s dinner: {DinnerWords(model, dinner)}", yes, reason, model.Deadline));
            }
        }

        var pages = new List<ExpectedPage> { new(1, new InkReadout(map), rules) };

        // Pages 2 and 3 (breakfast and lunch, lunchboxes): every line is its own item, approved by silence at the same deadline (D30).
        foreach (var (number, items) in MealSheetItems.Pages(model))
        {
            var pageMap = layouts[number - 1].Map;
            var itemRules = items.Select(item => (InkRule)new MealItemRule(item.Subject, item.YesId, item.ReasonId, model.Deadline)).ToList();
            pages.Add(new ExpectedPage(number, new InkReadout(pageMap), itemRules));
        }

        // The groceries pages (page 4 onward when the daily meals are there): every standing staple is a plain box, "need more" when ticked, and the blank
        // lines take a word that becomes a new staple. A page with none of them on it (a continuation of the list alone) has nothing to read.
        if (model.Groceries is { } groceries)
        {
            var slots = MealSheetGroceryPages.Slots(groceries);
            var first = 1 + (model.Daily is null ? 0 : 2);
            var last = layouts.Count - (model.AskAhead is null ? 0 : 1);
            for (var index = first; index < last; index++)
            {
                var pageMap = layouts[index].Map;
                var stapleRules = slots.Where(slot => pageMap.TryGet(slot.Id) is not null)
                    .Select(slot => (InkRule)new StapleRule(slot.Subject, slot.Id)).ToList();
                if (stapleRules.Count > 0)
                {
                    pages.Add(new ExpectedPage(index + 1, new InkReadout(pageMap), stapleRules));
                }
            }
        }

        return new ExpectedDocument(DeliveryKind.Meals, model.Week.WeekStart, null, person, null, pages);
    }

    private static string DinnerWords(MealSheetModel model, DinnerChoice dinner) => dinner.Kind switch
    {
        DinnerKind.Cooked when dinner.Recipe is { } card && model.Dishes.TryGetValue(card, out var dish) => dish.Title.Trim(),
        DinnerKind.EatOut when model.Outings is not null && model.Outings.TryGetValue(dinner.Day, out var outing) => "out at " + outing.Place.Trim(),
        DinnerKind.EatOut => "eating out",
        DinnerKind.Leftovers => "leftovers",
        _ => "nothing planned",
    };

    // Soreness is a separate fact from whether the session was done, and a question about it must not hold the day open.
    private static IReadOnlyList<string> SessionTargetIds(IEnumerable<ExpectedPage> pages) =>
        pages.SelectMany(page => page.Rules)
            .Where(rule => rule is not StillSoreRule)
            .SelectMany(RuleTargets.SubjectIds)
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
