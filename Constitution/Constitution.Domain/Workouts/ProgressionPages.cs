using Constitution.Domain.Documents;
using Constitution.Domain.Ink;
using Constitution.Domain.ReadBack;

namespace Constitution.Domain.Workouts;

/// <summary>
/// One exercise page as the pen sees it: the exercise, and the ids of its Done box and its easy / right / hard
/// group, in the ids the resolver reports them under.
/// </summary>
public sealed record ExercisePageRef(Exercise Exercise, string DoneBoxId = WorkoutIds.Done, string RatingGroupId = WorkoutIds.Rating);

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
/// Turns the answers for a session's exercise pages into one <see cref="PageRead"/> each. A page the read did not
/// report at all is <see cref="PageVerdict.Unknown"/>, never <see cref="PageVerdict.NotDone"/>: a page that was not read
/// is not silence (the resolver's own rule), so nothing here may call it a skip.
///
/// <para><b>The pages are told apart by their number, never by an id.</b> Every exercise page prints its Done tick and its
/// easy / right / hard choice under the same ids (<see cref="WorkoutIds.Done"/>, <see cref="WorkoutIds.Rating"/>), so an id
/// cannot say which page answered; reading by id alone gave the first page's answer to every exercise. Exercise <c>n</c>
/// (counting from 0) is page <see cref="FirstExercisePage"/> + <c>n</c> of the document: page 1 is the cover, the same way
/// <see cref="AssessmentBaselines.FromPages"/> finds a test by its page.</para>
/// </summary>
public static class PageReading
{
    /// <summary>The cover is page 1 and carries no exercise, so the first exercise is page 2.</summary>
    public const int FirstExercisePage = 2;

    /// <summary>The page of the document that holds the exercise at <paramref name="index"/> (from 0) of the session.</summary>
    public static int PageOfExercise(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return FirstExercisePage + index;
    }

    /// <summary>One read per exercise page, from the pages of a read document, each matched to its exercise by page number.</summary>
    public static IReadOnlyList<PageRead> Read(SessionPages session, IEnumerable<PageResolutions> pages)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pages);

        var byNumber = pages.ToLookup(page => page.Number, page => page.Resolutions);
        return session.Pages
            .Select((page, index) => ReadPage(page, byNumber[PageOfExercise(index)].SelectMany(list => list).ToList()))
            .ToList();
    }

    /// <summary>
    /// The same, from answers that arrive without their page. That is only unambiguous when no answer can belong to two
    /// pages, so an answer whose id is shared by several of the session's pages is refused rather than given to the first.
    /// </summary>
    public static IReadOnlyList<PageRead> Read(SessionPages session, IEnumerable<InkResolution> resolutions) =>
        Read(session, ByPage(session, resolutions));

    internal static IReadOnlyList<PageResolutions> ByPage(SessionPages session, IEnumerable<InkResolution> resolutions)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(resolutions);

        var perPage = session.Pages.Select(_ => new List<InkResolution>()).ToList();
        foreach (var resolution in resolutions)
        {
            var owners = session.Pages
                .Select((page, index) => (page, index))
                .Where(pair => pair.page.DoneBoxId == resolution.SubjectId || pair.page.RatingGroupId == resolution.SubjectId)
                .Select(pair => pair.index)
                .ToList();
            if (owners.Count > 1)
            {
                throw new ArgumentException(
                    $"The answer for '{resolution.SubjectId}' could belong to any of {owners.Count} pages, and nothing says which page it came from; give the answers page by page.",
                    nameof(resolutions));
            }

            if (owners.Count == 1)
            {
                perPage[owners[0]].Add(resolution);
            }
        }

        return perPage.Select((list, index) => new PageResolutions(PageOfExercise(index), list)).ToList();
    }

    // The answers of one page only, so its own ids are enough.
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
