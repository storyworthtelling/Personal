using Constitution.Domain.Household;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Templates;

/// <summary>The library after the sheet's answers, the likes a tick taught, and the written words for the judge.</summary>
public sealed record TemplateFeedbackResult(TemplateLibrary Library, IReadOnlyList<TasteNote> Likes, IReadOnlyList<TasteEvidence> Evidence);

/// <summary>
/// Turns a meal-sheet decision into template status changes and taste evidence (D110). Pure.
///
/// <para><b>What each answer comes to.</b> A tick that stands (the deadline passed, nobody said no) approves the template as
/// <see cref="TemplateApproval.Explicit"/> and teaches one <i>likes</i> dish note per adult who ticked it. Silence at the deadline approves it
/// as <see cref="TemplateApproval.Silence"/> (D103) and teaches nothing: it is weaker evidence than a tick, and a later tick lifts it. A no rejects
/// the template, keeps the written words as its reason, and hands each adult's words to the judge as <see cref="TasteEvidence"/> exactly as a
/// denial of a dinner does. A line still waiting on the deadline changes nothing.</para>
///
/// <para><b>The week.</b> The decision names it when any answer was read; when no tablet was read at all (D103: everything is approved by silence) the caller
/// passes the sheet's own week as <c>sheetWeek</c>, and the lines still settle.</para>
///
/// <para><b>Idempotent per template and week.</b> A template remembers the week whose answers it has taken (<see cref="MealTemplate.FeedbackWeek"/>); the
/// same decision applied to the result changes and emits nothing. Rejected templates stay rejected. Only lines the library says it printed that
/// week are read, so a dinner's answer is never taken for a template's.</para>
/// </summary>
public static class TemplateFeedback
{
    public static TemplateFeedbackResult From(MealDecision decision, IReadOnlyList<MealAnswer> answers, TemplateLibrary library, DateOnly day, DateOnly? sheetWeek = null)
    {
        ArgumentNullException.ThrowIfNull(decision);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(library);
        var likes = new List<TasteNote>();
        var evidence = new List<TasteEvidence>();
        if ((decision.Week ?? sheetWeek) is not { } week)
        {
            return new TemplateFeedbackResult(library, likes, evidence);
        }

        var result = library;
        foreach (var print in library.Prints.Where(print => print.Week == week))
        {
            if (decision.Item(print.ItemId) is not { } item
                || library.Find(print.TemplateId) is not { } template
                || template.FeedbackWeek >= week
                || item.Outcome == MealItemOutcome.Pending)
            {
                continue;
            }

            var updated = item.Outcome == MealItemOutcome.Denied
                ? Rejection(template, item, evidence, day)
                : Approval(template, item, answers.Where(answer => answer.Week == week && answer.ItemId == print.ItemId), likes, day);
            result = result.With(updated with { FeedbackWeek = week });
        }

        return new TemplateFeedbackResult(result, likes, evidence);
    }

    private static MealTemplate Rejection(MealTemplate template, MealItemDecision item, List<TasteEvidence> evidence, DateOnly day)
    {
        var reasons = new List<string>();
        foreach (var denial in item.Denials.Where(denial => !string.IsNullOrWhiteSpace(denial.Reason)))
        {
            var words = denial.Reason!.Trim();
            reasons.Add(words);
            evidence.Add(new TasteEvidence(
                new PersonId(denial.Person),
                TasteEvidenceKind.Denial,
                words.Length > TasteEvidence.MaxWordsLength ? words[..TasteEvidence.MaxWordsLength] : words,
                new RecipeId(template.Id),
                day));
        }

        var joined = string.Join("; ", reasons.Distinct(StringComparer.Ordinal));
        return template with
        {
            Status = TemplateStatus.Rejected,
            Approval = TemplateApproval.None,
            Reason = joined.Length == 0 ? null : joined.Length > MealTemplate.MaxReasonLength ? joined[..MealTemplate.MaxReasonLength] : joined,
        };
    }

    private static MealTemplate Approval(MealTemplate template, MealItemDecision item, IEnumerable<MealAnswer> answers, List<TasteNote> likes, DateOnly day)
    {
        if (template.Status == TemplateStatus.Rejected)
        {
            return template;
        }

        if (item.Basis != MealApprovalBasis.Tick)
        {
            return template.Status == TemplateStatus.Proposed
                ? template with { Status = TemplateStatus.Approved, Approval = TemplateApproval.Silence }
                : template;
        }

        var ticked = answers
            .Where(answer => answer.Verdict == MealVerdict.Approved && !item.Ignored.Contains(answer))
            .Select(answer => answer.Person)
            .Distinct()
            .OrderBy(person => person.Value, StringComparer.Ordinal);
        likes.AddRange(ticked.Select(person => new TasteNote(person, TasteSubjectKind.Dish, template.Id, TasteStance.Likes, TasteOrigin.Learned, day)));
        return template with { Status = TemplateStatus.Approved, Approval = TemplateApproval.Explicit, Reason = null };
    }
}
