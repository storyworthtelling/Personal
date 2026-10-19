using Constitution.Domain.Ink;

namespace Constitution.Domain.Workouts;

/// <summary>
/// One exercise page as the pen sees it: the exercise, and the ids of its Done box and its easy / right / hard
/// group, in the ids the resolver reports them under.
/// </summary>
public sealed record ExercisePageRef(Exercise Exercise, string DoneBoxId, string RatingGroupId);

/// <summary>A session's exercise pages in the order they were printed, for one person on one day.</summary>
public sealed record SessionPages(string Person, DateOnly Day, SessionKind Kind, IReadOnlyList<ExercisePageRef> Pages);

/// <summary>What the read says about one page: done, not done, or no answer yet.</summary>
public enum PageVerdict
{
    /// <summary>Ticked, or a circled word with no tick (p.5: attempted counts as done, D18).</summary>
    Done,

    /// <summary>Read, sure, and empty.</summary>
    NotDone,

    /// <summary>Not read, or waiting on a question: neither done nor skipped.</summary>
    Unknown,
}

/// <summary>One page's verdict and, when the owner circled a word of the three, its rating.</summary>
public sealed record PageRead(ExercisePageRef Page, PageVerdict Verdict, ExerciseRating? Rating);

/// <summary>
/// Turns the resolver's answers for a session's exercise pages into one <see cref="PageRead"/> each. A page the
/// resolver did not report at all is <see cref="PageVerdict.Unknown"/>, never <see cref="PageVerdict.NotDone"/>: a page
/// that was not read is not silence (the resolver's own rule), so nothing here may call it a skip.
/// </summary>
public static class PageReading
{
    public static IReadOnlyList<PageRead> Read(SessionPages session, IEnumerable<InkResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolutions);

        var answers = resolutions.ToList();
        return session.Pages.Select(page => ReadPage(page, answers)).ToList();
    }

    private static PageRead ReadPage(ExercisePageRef page, List<InkResolution> answers)
    {
        // A circled word with no tick: the page is done and the word is its rating.
        if (answers.OfType<InkResolution.PageDone>().FirstOrDefault(item => item.SubjectId == page.DoneBoxId) is { } circledAlone)
        {
            return new PageRead(page, PageVerdict.Done, ExerciseRatings.FromWord(circledAlone.Rating));
        }

        var rating = answers.OfType<InkResolution.Circled>().FirstOrDefault(item => item.SubjectId == page.RatingGroupId) is { } circled
            ? ExerciseRatings.FromWord(circled.Word)
            : null;

        var verdict = answers.Where(item => item.SubjectId == page.DoneBoxId).Select(item => item switch
        {
            InkResolution.Ticked => PageVerdict.Done,
            InkResolution.Skipped => PageVerdict.NotDone,
            _ => PageVerdict.Unknown,
        }).DefaultIfEmpty(PageVerdict.Unknown).First();

        return new PageRead(page, verdict, rating);
    }
}
