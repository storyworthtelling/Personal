using Constitution.Domain.Notices;

namespace Constitution.Domain.Meals;

/// <summary>Where "I ate something else" is. There is no phase that is a screen: the camera, then the card says so, then the card says the result.</summary>
public enum AtePhase
{
    Idle,
    Capturing,
    Estimating,
    Shown,
    Said,
}

/// <summary>One line on the dinner card: a status in the teal voice, or the result with its figure in mono and nothing beneath (D107).</summary>
public sealed record AteLine(string Lead, string? Figure, bool IsStatus)
{
    public string Text => Lead + Figure;
}

/// <summary>
/// "I ate something else" (C-6.6.1, book p.15) as the card sees it: one photo, "Estimating your dinner…" on the meal's card, then
/// "You had something else · about 900 kcal" on the same card, with nothing in between to confirm. The meal comes from the time
/// the camera opened (D66) and the estimate is about this install's own intake and no one's else (D65): there is no person anywhere in
/// it. The planned dish is not touched. Time is passed in; the photo is the head's and the model sees only its path.
/// </summary>
public sealed class AteSomethingElseModel
{
    private readonly List<AteEstimate> estimates = [];
    private AteMeal? meal;
    private DateOnly day;
    private string? sentence;

    public AtePhase Phase { get; private set; } = AtePhase.Idle;

    /// <summary>The meal of the capture in hand, or null before one starts.</summary>
    public AteMeal? Meal => meal;

    /// <summary>The day of the capture in hand.</summary>
    public DateOnly Day => day;

    /// <summary>False while a photo is being taken or estimated: one at a time.</summary>
    public bool CanStart => Phase is not (AtePhase.Capturing or AtePhase.Estimating);

    /// <summary>Replaces what is shown from what was kept. Called when the page opens.</summary>
    public void Load(IEnumerable<AteEstimate> stored)
    {
        ArgumentNullException.ThrowIfNull(stored);
        estimates.Clear();
        estimates.AddRange(stored);
    }

    /// <summary>The camera is about to open. The meal and the day are read off this moment.</summary>
    public bool StartCapture(DateTimeOffset now)
    {
        if (!CanStart)
        {
            return false;
        }

        meal = AteMeals.At(TimeOnly.FromTimeSpan(now.TimeOfDay));
        day = DateOnly.FromDateTime(now.DateTime);
        sentence = null;
        Phase = AtePhase.Capturing;
        return true;
    }

    /// <summary>The camera gave a photo back: the card says it is estimating, before the estimate has even started.</summary>
    public void PhotoTaken()
    {
        sentence = null;
        Phase = AtePhase.Estimating;
    }

    /// <summary>The camera was backed out of. Quiet: the card goes back to what it said before.</summary>
    public void CaptureBackedOut()
    {
        sentence = null;
        Phase = AtePhase.Idle;
    }

    /// <summary>The camera would not open or gave nothing back; the card says so in a sentence.</summary>
    public void CaptureFailed(string why)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        sentence = why;
        Phase = AtePhase.Said;
    }

    /// <summary>
    /// Asks the estimator about the photograph and lets the photograph go, whatever happens. A service that cannot be reached is a
    /// sentence, not an exception; anything else reaches the caller with its stack, after the photo is gone.
    /// </summary>
    public async Task<AteOutcome> EstimateAsync(
        IAteEstimator estimator,
        string cacheRoot,
        string photoPath,
        Func<string, bool> delete,
        Action<bool>? forgotten = null,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(estimator);
        Phase = AtePhase.Estimating;
        sentence = null;
        try
        {
            return await AtePhoto.UseAndForgetAsync(cacheRoot, photoPath, path => estimator.EstimateAsync(path, cancellation), delete, forgotten)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        {
            // The type is for the caller's trail; the sentence is fixed and quotes nothing.
            return new AteOutcome.Unavailable(AteText.UnreachableSentence);
        }
    }

    /// <summary>The estimate came back. A reading becomes the card's result and is returned to be kept; anything else is a sentence.</summary>
    public AteEstimate? Finish(AteOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        if (outcome is AteOutcome.Read read && meal is { } which)
        {
            var kept = new AteEstimate(day, which, read.Reading);
            estimates.RemoveAll(item => item.Day == kept.Day && item.Meal == kept.Meal);
            estimates.Add(kept);
            sentence = null;
            Phase = AtePhase.Shown;
            return kept;
        }

        sentence = AteText.Sentence(outcome);
        Phase = AtePhase.Said;
        return null;
    }

    /// <summary>The estimate was let go (the dog-ear's fold was turned): it leaves the card and the one it replaced comes back.</summary>
    public void Withdraw(AteEstimate recorded, AteEstimate? previous)
    {
        ArgumentNullException.ThrowIfNull(recorded);
        estimates.RemoveAll(item => item.Day == recorded.Day && item.Meal == recorded.Meal);
        if (previous is not null)
        {
            estimates.Add(previous);
        }

        sentence = null;
        Phase = AtePhase.Idle;
    }

    /// <summary>
    /// What the dinner card says on a day now, or null when it says nothing (D107). Only the dinner has a card; breakfast and lunch are
    /// recorded but never drawn, and speak through <see cref="StatusFor"/> instead.
    /// </summary>
    public AteLine? LineFor(AteMeal card, DateOnly onDay)
    {
        if (card != AteMeal.Dinner)
        {
            return null;
        }

        if (meal == card && day == onDay)
        {
            if (Phase == AtePhase.Estimating)
            {
                return new AteLine(AteText.Estimating(card), null, true);
            }

            if (Phase == AtePhase.Said && sentence is not null)
            {
                return new AteLine(sentence, null, true);
            }
        }

        var kept = estimates.FirstOrDefault(item => item.Day == onDay && item.Meal == card);
        return kept is null ? null : new AteLine(AteText.ResultLead, AteText.Figure(kept.Reading.KcalAbout), false);
    }

    /// <summary>The meals that have a card on a day: the dinner, when it has something to say.</summary>
    public IReadOnlyList<AteMeal> MealsWithALine(DateOnly onDay) =>
        Enum.GetValues<AteMeal>().Where(card => LineFor(card, onDay) is not null).ToList();

    /// <summary>
    /// The one plain sentence for a breakfast or lunch photo (D107): estimating, a failure, or "Noted for lunch." once it is recorded.
    /// Null for a dinner photo (its card speaks) and for any day other than the capture's.
    /// </summary>
    public string? StatusFor(DateOnly onDay)
    {
        if (meal is not { } which || which == AteMeal.Dinner || day != onDay)
        {
            return null;
        }

        return Phase switch
        {
            AtePhase.Estimating => AteText.Estimating(which),
            AtePhase.Said => sentence,
            AtePhase.Shown => AteText.Noted(which),
            _ => null,
        };
    }
}

/// <summary>
/// Letting go of an estimate, undo-first (DESIGN.md section 5; the same shape as the recipe scan's). An estimate is kept at once and
/// the shared dog-ear's window is the time to take it back: a turn of the fold hands back what was recorded and what it replaced.
/// Time is passed in.
/// </summary>
public sealed class AteUndo
{
    public const string ActKey = "ate:estimate";

    private DateTimeOffset expires;

    public AteUndoStep? Pending { get; private set; }

    public void Begin(NoticeBoard board, AteEstimate recorded, AteEstimate? previous, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(board);
        ArgumentNullException.ThrowIfNull(recorded);
        Pending = new AteUndoStep(recorded, previous);
        expires = now + NoticeBoard.UndoWindow;
        board.BeginUndo(ActKey, now);
    }

    /// <summary>The fold was turned and the board gave back this key: what to take back, or null when the act was not ours.</summary>
    public AteUndoStep? Reverse(string actKey)
    {
        if (Pending is not { } waiting || actKey != ActKey)
        {
            return null;
        }

        Pending = null;
        return waiting;
    }

    /// <summary>The window has closed and the estimate stays. Returns whether there was one waiting.</summary>
    public bool Expire(DateTimeOffset now)
    {
        if (Pending is null || now < expires)
        {
            return false;
        }

        Pending = null;
        return true;
    }
}

/// <summary>What a turn of the fold takes back.</summary>
public sealed record AteUndoStep(AteEstimate Recorded, AteEstimate? Previous);
