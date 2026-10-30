using Constitution.Domain.Calendar;
using Constitution.Domain.Recipes;

namespace Constitution.Domain.Meals.Pipeline;

/// <summary>What one run of the deadline step is told: the moment on the household's clock, and who the adults are. Everything else is the step's own reads.</summary>
public sealed record MealDeadlineInputs(DateTimeOffset Now, PlanningRules Rules);

/// <summary>Why the step had no week to decide. Not an error.</summary>
public enum MealDeadlineNothingBecause
{
    /// <summary>No planned week is kept.</summary>
    NoWeekKept,

    /// <summary>The newest kept week's deadline has not passed.</summary>
    BeforeTheDeadline,

    /// <summary>Every week whose deadline has passed is decided and its evidence is judged.</summary>
    AlreadyDecided,
}

/// <summary>Why the step stopped with nothing decided. A closed list: the trail prints the word.</summary>
public enum MealDeadlineStoppedBecause
{
    /// <summary>The record of decided weeks is there and cannot be read; read as empty it would decide a week twice.</summary>
    DecisionsUnreadable,

    /// <summary>The saved weeks are there and cannot be read.</summary>
    WeeksUnreadable,

    /// <summary>The household file cannot be read, or holds an answer record for the week this build cannot read: an unread no would be approved.</summary>
    AnswersUnreadable,

    /// <summary>The template library is there and cannot be read.</summary>
    LibraryUnreadable,

    /// <summary>The taste notes could not be written.</summary>
    NotesNotSaved,

    /// <summary>The template library could not be written.</summary>
    LibraryNotSaved,
}

/// <summary>What a deciding run did. Counts and card ids only.</summary>
public sealed record MealDeadlineReport(
    DateOnly Week,
    int Approved,
    int Denied,
    int ApprovedUnread,
    int LateSetAside,
    int TemplatesFed,
    int LikesTaught,
    int Lasting,
    int OneOff,
    int Unjudged,
    bool NeedsRevisedSheet,
    IReadOnlyList<RecipeId> ReplanWithout,
    bool WasRetry);

/// <summary>The result of one run of the deadline step.</summary>
public abstract record MealDeadlineOutcome
{
    private MealDeadlineOutcome()
    {
    }

    /// <summary>Only the install holding the planning lease decides. The partner's phone lands here, which is not an error.</summary>
    public sealed record NotThePlanner(string Sentence) : MealDeadlineOutcome;

    public sealed record NothingToDecide(MealDeadlineNothingBecause Because, string Sentence) : MealDeadlineOutcome;

    /// <summary>Nothing was decided or recorded. The sentence is fixed words and quotes nothing the household wrote.</summary>
    public sealed record Stopped(MealDeadlineStoppedBecause Because, string Sentence) : MealDeadlineOutcome;

    public sealed record Decided(MealDeadlineReport Report) : MealDeadlineOutcome;
}

/// <summary>What reading a week's meal answers gave: the answers and how many records for that week could not be read, or the reason the file could not be read.</summary>
public abstract record MealAnswersRead
{
    private MealAnswersRead()
    {
    }

    public sealed record Held(IReadOnlyList<MealAnswer> Answers, int UnreadableRecords) : MealAnswersRead;

    public sealed record Unreadable(string Reason) : MealAnswersRead;
}

/// <summary>Where the two adults' meal answers are read from: the household file.</summary>
public interface IMealAnswerSource
{
    MealAnswersRead Read(DateOnly week);
}

/// <summary>What became of a write of taste notes.</summary>
public abstract record MealNotesSave
{
    private MealNotesSave()
    {
    }

    public sealed record Saved(int Written) : MealNotesSave;

    /// <summary>Nothing was written; the sentence says why.</summary>
    public sealed record Refused(string Sentence) : MealNotesSave;
}

/// <summary>Where the taste notes the deadline teaches are kept: records in the household file. Saving the same note again changes nothing.</summary>
public interface IMealTasteNotes
{
    MealNotesSave Save(IReadOnlyList<TasteNote> notes);
}

/// <summary>The judge used until a real one is built: nobody answers, so every piece of evidence is unjudged and asked again next time.</summary>
public sealed class UnavailableTasteJudge : ITasteJudge
{
    public Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation) =>
        Task.FromResult<TasteJudgement>(new TasteJudgement.Unavailable(Workouts.AuthorUnavailable.NotConfigured));
}
