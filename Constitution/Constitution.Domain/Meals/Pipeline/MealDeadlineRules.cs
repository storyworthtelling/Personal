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
    public static IReadOnlyList<TasteEvidence> DinnerEvidence(MealDecision decision, StoredMealWeek week, DateOnly day)
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
                    dinner.Kind == DinnerKind.Cooked ? dinner.Recipe : null,
                    day));
            }
        }

        return evidence;
    }

    /// <summary>
    /// Whether the printed sheet still shows a line somebody refused: a denied dinner whose evening is today or ahead, or a denied line of pages 2 and 3
    /// while the week runs. A refused dinner that is already over needs nothing.
    /// </summary>
    public static bool NeedsRevisedSheet(MealDecision decision, StoredMealWeek week, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(week);
        return decision.Denied.Any(item => DinnerOf(week, item.ItemId) is { } dinner
            ? dinner.Day >= today
            : today <= week.WeekStart.AddDays(MealWeek.Evenings - 1));
    }

    /// <summary>The denied dinners that still have an evening ahead (today counts), by card: what a replan of them must not serve.</summary>
    public static IReadOnlyList<RecipeId> DeniedAhead(MealDecision decision, StoredMealWeek week, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(week);
        return decision.Denied
            .Select(item => DinnerOf(week, item.ItemId))
            .OfType<DinnerChoice>()
            .Where(dinner => dinner.Kind == DinnerKind.Cooked && dinner.Day >= today)
            .Select(dinner => dinner.Recipe!.Value)
            .Distinct()
            .ToList();
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

        var tail = report.NeedsRevisedSheet ? ", revised sheet needed" : string.Empty;
        return $"meal deadline decided, {report.Approved} lines approved, {report.Denied} lines denied, {report.ApprovedUnread} lines unread, "
            + $"{report.LateSetAside} lines late, {report.TemplatesFed} templates fed, {report.LikesTaught} likes taught, {report.Unjudged} words unjudged{tail}";
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
            default:
                throw new ArgumentOutOfRangeException(nameof(because), because, "This reason has no trail words; add them here.");
        }
    }
}
