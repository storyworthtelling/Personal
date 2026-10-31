using Constitution.Domain.Calendar;
using Constitution.Domain.Meals;
using Constitution.Domain.Meals.Pipeline;
using Constitution.Domain.Meals.Templates;
using Constitution.Domain.Workouts;
using Constitution.Domain.Workouts.Pipeline;

namespace Constitution.Infrastructure.Meals;

/// <summary>What the templates step reaches: the library and the author. No token, no passphrase.</summary>
/// <param name="Library">The library over the household file.</param>
/// <param name="Author">Proposes new templates; <see cref="NoTemplateAuthor"/> until one is built.</param>
public sealed record MealTemplateSeams(ITemplateLibraryStore Library, ITemplateAuthor Author);

/// <summary>
/// The rotating-templates step of the weekly meal run (D110): chooses the week's breakfasts, lunches and lunchbox for pages 2 and 3 of the sheet, asks the
/// author for new lines when the library is short, and remembers what it printed. It runs only on the phone that plans the week and only when the run is going
/// to build a sheet, after the week is kept and before the paper is made.
///
/// <para><b>Order.</b> Load the library; if the sheet's deadline has passed with nothing ever printed for this week, stop (a proposal marked as shown on a sheet
/// nobody will see would be answered by silence). Otherwise ask the author for what is short (never more than <see cref="TemplateProposals.MaxOutstanding"/> waiting, never
/// twice for a week already printed, never while a template record cannot be read), merge what it proposes as waiting, let <see cref="TemplateRotation"/> choose, and save the
/// library with the new prints <b>before</b> the paper is made, so a stop later never loses it and a revised sheet reprints the same proposal.</para>
///
/// <para><b>Fail closed.</b> A library that cannot be read is typed, never read as empty; a library that cannot be saved prints nothing, since a line that cannot be
/// remembered cannot be answered. With no approved template and no author the pages are simply left out and nothing is marked printed. An unchanged re-run saves nothing.
/// The author sees only words (<see cref="TemplateRequest"/>); the taste hints it may take are not wired yet, so none are sent.</para>
/// </summary>
public static class MealTemplateStep
{
    public static async Task<MealTemplatesResult> RunAsync(
        DateOnly week, PlanningRules rules, DateTimeOffset now, MealTemplateSeams seams, IWorkoutWeekLog log, CancellationToken cancellation)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(seams);
        ArgumentNullException.ThrowIfNull(log);
        cancellation.ThrowIfCancellationRequested();

        var result = await RunStepsAsync(week, rules, now, seams, cancellation);
        log.Outcome(MealTemplateTrail.LogPhrase(result));
        return result;
    }

    private static async Task<MealTemplatesResult> RunStepsAsync(
        DateOnly week, PlanningRules rules, DateTimeOffset now, MealTemplateSeams seams, CancellationToken cancellation)
    {
        TemplateLibraryRead.Held held;
        switch (seams.Library.Load())
        {
            case TemplateLibraryRead.Held read:
                held = read;
                break;
            case TemplateLibraryRead.NoneYet:
                return new MealTemplatesResult(TemplateRunStatus.NoLibraryYet, 0, 0, null);
            case TemplateLibraryRead.Unreadable:
                return new MealTemplatesResult(TemplateRunStatus.LibraryUnreadable, 0, 0, null);
            default:
                throw new InvalidOperationException("The template library came back as neither a library nor a reason.");
        }

        var library = held.Library;
        var unreadable = held.UnreadableRecords;
        var printedThisWeek = library.Prints.Any(print => print.Week == week);
        if (!printedThisWeek && now >= MealDeadline.ForWeek(week, rules.Zone))
        {
            return new MealTemplatesResult(TemplateRunStatus.PastDeadline, 0, 0, null) { UnreadableRecords = unreadable };
        }

        var labels = TemplateRunRules.Labels(rules);
        var proposed = 0;
        AuthorUnavailable? reason = null;
        var want = TemplateRunRules.Wants(library, labels.Count, labels.Count);
        if (want.Count > 0 && !printedThisWeek && unreadable == 0)
        {
            var authored = await seams.Author.ProposeAsync(TemplateRequest.From(library, [], want.Slots, want.Count), cancellation);
            switch (authored)
            {
                case TemplateAuthored.Drafts drafts:
                    var merged = TemplateProposals.Merge(library, drafts.Proposals);
                    library = merged.Library;
                    proposed = merged.Added.Count;
                    break;
                case TemplateAuthored.Unavailable unavailable:
                    reason = unavailable.Reason;
                    break;
                default:
                    throw new InvalidOperationException("The template author came back as neither proposals nor a reason.");
            }
        }

        var facts = new TemplateRotationFacts(labels, labels)
        {
            LastWeek = library.Prints.Where(print => print.Week == week.AddDays(-7)).ToList(),
        };
        var picks = TemplateRotation.Pick(library, week, facts);
        if (picks.Status == TemplatePickStatus.Ready)
        {
            library = library.WithPrints(picks.Prints);
        }

        if (!TemplateRunRules.Same(held.Library, library) && seams.Library.Save(library) is TemplateSave.Refused)
        {
            return new MealTemplatesResult(TemplateRunStatus.NotSaved, 0, 0, null) { AuthorReason = reason, UnreadableRecords = unreadable };
        }

        var status = picks.Status switch
        {
            TemplatePickStatus.Ready => TemplateRunStatus.Printed,
            TemplatePickStatus.Incomplete => TemplateRunStatus.Incomplete,
            _ => TemplateRunStatus.NothingYet,
        };
        return new MealTemplatesResult(status, proposed, picks.Prints.Count, picks.Daily) { AuthorReason = reason, UnreadableRecords = unreadable };
    }
}
