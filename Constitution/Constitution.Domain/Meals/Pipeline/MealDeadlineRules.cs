using Constitution.Domain.Documents;
using Constitution.Domain.Household;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>The deadline step's pure rules.</summary>
public static class MealDeadlineRules
{
    /// <summary>
    /// The lines the printed sheet asked about for this week: the YES box of each dinner by weekday, then the YES box of each template line the library
    /// says it printed that week (in id order). A test holds this equal to <see cref="MealAnswerRecords.ExpectedItems"/> of the built sheet.
    /// </summary>
    public static IReadOnlyList<string> ExpectedLines(StoredMealWeek week, TemplateLibrary? library)
    {
        ArgumentNullException.ThrowIfNull(week);
        var lines = week.Week.Dinners.Select(dinner => MealSheetIds.DinnerYes(dinner.Day.DayOfWeek)).ToList();
        if (library is not null)
        {
            lines.AddRange(library.Prints
                .Where(print => print.Week == week.WeekStart)
                .Select(print => print.ItemId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal));
        }

        return lines;
    }

    /// <summary>
    /// The written words of every denied dinner as evidence for the judge, one per denying adult who wrote any. A tick, a silence and a denial without
    /// words are not evidence. The item id names a weekday; the week gives the card.
    /// </summary>
    /// <param name="marked">The cooked evenings an earlier run left unplanned (D111), whose cards the kept week no longer names.</param>
    public static IReadOnlyList<TasteEvidence> DinnerEvidence(MealDecision decision, StoredMealWeek week, DateOnly day, IReadOnlyList<DeniedDish>? marked = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(week);
        var evidence = new List<TasteEvidence>();
        foreach (var item in decision.Denied)
        {
            if (DinnerOf(week, item.ItemId) is not { } dinner)
            {
                continue;
            }

            foreach (var denial in item.Denials.Where(denial => !string.IsNullOrWhiteSpace(denial.Reason)))
            {
                var words = denial.Reason!.Trim();
                evidence.Add(new TasteEvidence(
                    new PersonId(denial.Person),
                    TasteEvidenceKind.Denial,
                    words.Length > TasteEvidence.MaxWordsLength ? words[..TasteEvidence.MaxWordsLength] : words,
                    dinner.Kind == DinnerKind.Cooked
                        ? dinner.Recipe
                        : marked?.FirstOrDefault(dish => dish.Evening == DeniedEveningWords.Of(dinner.Day.DayOfWeek))?.Card,
                    day));
            }
        }

        return evidence;
    }

    /// <summary>
    /// D111: the dinners the household denied whose evening is today or ahead, as the kept week has them, in week order. Not a dish test: a denied evening the
    /// plan already left empty is still an evening whose dinner somebody has to see to.
    /// </summary>
    public static IReadOnlyList<DinnerChoice> DeniedEveningsAhead(MealDecision decision, StoredMealWeek week, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(week);
        return decision.Denied
            .Select(item => DinnerOf(week, item.ItemId))
            .OfType<DinnerChoice>()
            .Where(dinner => dinner.Day >= today)
            .DistinctBy(dinner => dinner.Day)
            .OrderBy(dinner => dinner.Day)
            .ToList();
    }

    /// <summary>D111: the week with each of these evenings left unplanned ("Not approved."), every other evening and the dishes the rest still name untouched.</summary>
    public static StoredMealWeek LeaveUnplanned(StoredMealWeek week, IReadOnlyList<DateOnly> days)
    {
        ArgumentNullException.ThrowIfNull(week);
        ArgumentNullException.ThrowIfNull(days);
        var changed = false;
        var dinners = new List<DinnerChoice>();
        foreach (var dinner in week.Week.Dinners)
        {
            if (days.Contains(dinner.Day) && dinner.Kind != DinnerKind.NothingPlanned)
            {
                dinners.Add(DinnerChoice.NothingPlanned(dinner.Day, DeniedEveningWords.NotApprovedReason));
                changed = true;
            }
            else
            {
                dinners.Add(dinner);
            }
        }

        if (!changed)
        {
            return week;
        }

        var named = dinners.Where(dinner => dinner.Recipe is not null).Select(dinner => dinner.Recipe!.Value).ToHashSet();
        return new StoredMealWeek(
            MealWeek.Planned(week.WeekStart, dinners, week.Week.Notes),
            week.PlannedAt,
            week.Dishes.Where(dish => named.Contains(dish.Recipe)).ToList());
    }

    private static DinnerChoice? DinnerOf(StoredMealWeek week, string itemId) =>
        week.Week.Dinners.FirstOrDefault(dinner => string.Equals(MealSheetIds.DinnerYes(dinner.Day.DayOfWeek), itemId, StringComparison.Ordinal));
}

/// <summary>The veto a replan of the denied evenings needs, in the planner's own terms.</summary>
public static class ReplanVeto
{
    /// <summary>
    /// The request with each of <paramref name="dishes"/> added to <see cref="MealWeekRequest.NeverAgain"/> for every adult, for this request only. It is the planner's
    /// existing never-again mechanism scoped to one request, so nothing is stored and the dish is served again next week.
    /// </summary>
    public static MealWeekRequest Apply(MealWeekRequest request, IReadOnlyList<RecipeId> dishes)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(dishes);
        if (dishes.Count == 0)
        {
            return request;
        }

        var everyone = request.Rules.Adults.Select(adult => adult.Name).ToList();
        var merged = request.NeverAgain.ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var dish in dishes)
        {
            var people = merged.TryGetValue(dish, out var existing)
                ? existing.Concat(everyone).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : everyone.ToHashSet(StringComparer.OrdinalIgnoreCase);
            merged[dish] = people;
        }

        return request with { NeverAgain = merged };
    }
}

/// <summary>The deadline step's words in the Body trail: fixed phrases and counts, never a dish, a name or a date. Lower case.</summary>
public static class MealDeadlineTrail
{
    public static string LogPhrase(MealDeadlineOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        switch (outcome)
        {
            case MealDeadlineOutcome.NotThePlanner:
                return "meal deadline not decided, not the planner this week";
            case MealDeadlineOutcome.NothingToDecide nothing:
                return "meal deadline nothing to decide, " + NothingWords(nothing.Because);
            case MealDeadlineOutcome.Stopped stopped:
                return "meal deadline stopped, " + StoppedWords(stopped.Because);
            case MealDeadlineOutcome.Decided decided:
                return DecidedPhrase(decided.Report);
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "This outcome has no trail phrase; add one here.");
        }
    }

    private static string DecidedPhrase(MealDeadlineReport report)
    {
        if (report.WasRetry)
        {
            return $"meal deadline asked again, {report.Lasting} likes taught, {report.Unjudged} words unjudged";
        }

        return $"meal deadline decided, {report.Approved} lines approved, {report.Denied} lines denied, {report.ApprovedUnread} lines unread, "
            + $"{report.LateSetAside} lines late, {report.TemplatesFed} templates fed, {report.LikesTaught} likes taught, {report.Unjudged} words unjudged";
    }

    private static string NothingWords(MealDeadlineNothingBecause because)
    {
        switch (because)
        {
            case MealDeadlineNothingBecause.NoWeekKept:
                return "no week kept";
            case MealDeadlineNothingBecause.BeforeTheDeadline:
                return "before the deadline";
            case MealDeadlineNothingBecause.AlreadyDecided:
                return "already decided";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail words; add them here.");
        }
    }

    private static string StoppedWords(MealDeadlineStoppedBecause because)
    {
        switch (because)
        {
            case MealDeadlineStoppedBecause.DecisionsUnreadable:
                return "decisions unreadable";
            case MealDeadlineStoppedBecause.WeeksUnreadable:
                return "weeks unreadable";
            case MealDeadlineStoppedBecause.AnswersUnreadable:
                return "answers unreadable";
            case MealDeadlineStoppedBecause.LibraryUnreadable:
                return "library unreadable";
            case MealDeadlineStoppedBecause.NotesNotSaved:
                return "notes not saved";
            case MealDeadlineStoppedBecause.LibraryNotSaved:
                return "library not saved";
            case MealDeadlineStoppedBecause.NotConfigured:
                return "not configured";
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail words; add them here.");
        }
    }
}
