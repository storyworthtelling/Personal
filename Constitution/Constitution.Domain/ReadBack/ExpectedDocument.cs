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
    public ExpectedDocument(DeliveryKind kind, DateOnly day, string? subject, string person, SessionKind? session, IReadOnlyList<ExpectedPage> pages)
    {
        ArgumentNullException.ThrowIfNull(pages);
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
                if (Pages.Count > 0 || session is not null)
                {
                    throw new ArgumentException("The digest is never read: it holds no pages and no session.", nameof(pages));
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

    /// <summary>The title delivery uploaded it under, which is the title it is found by.</summary>
    public string Name => DeliveredName.For(Kind, Day, Subject);

    /// <summary>False for the digest, which is only ever trashed.</summary>
    public bool ReadsInk => Pages.Count > 0;

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
    /// tick or written reason, approved by silence at the model's deadline. Later pages of the sheet add their own rules.
    /// </summary>
    public static ExpectedDocument ForMealSheet(MealSheetModel model, string person)
    {
        ArgumentNullException.ThrowIfNull(model);

        var map = MealSheetDocument.Build(model)[0].Map;
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

        return new ExpectedDocument(DeliveryKind.Meals, model.Week.WeekStart, null, person, null, [new ExpectedPage(1, new InkReadout(map), rules)]);
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
