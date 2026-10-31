using Constitution.Domain.Workouts;

namespace Constitution.Domain.Meals;

/// <summary>
/// How hard the judge may be asked, so a broken key or a service that is down is never hammered. A pass asks at most <see cref="MostPerPass"/> questions and stops at
/// the first sign the service cannot answer (<see cref="BoundedTasteJudge"/>); a week's words are given up on after <see cref="MostAttempts"/> passes that reached
/// the judge and still left some unsettled. These are the agent's first guesses; nothing here has met a live service.
/// </summary>
public static class TasteJudgeLimits
{
    public const int MostPerPass = 6;

    public const int MostAttempts = 5;

    /// <summary>
    /// After a pass: the attempts used and whether the week still has words pending. A pass that never reached the judge (no key) uses none, so a key restored later
    /// still gets its turn; a pass with nothing unjudged uses none and leaves nothing pending.
    /// </summary>
    public static (int Attempts, bool Pending) After(int attempts, bool reached, int unjudged)
    {
        if (unjudged <= 0)
        {
            return (attempts, false);
        }

        var used = reached ? attempts + 1 : attempts;
        return (used, used < MostAttempts);
    }
}

/// <summary>
/// Wraps a judge for one pass. It forwards at most <c>most</c> questions; an answer that says the service cannot be reached, refused the key or has no key ends the
/// asking for the rest of the pass (the remaining words are unjudged and asked again next pass); an unusable reply to one question does not, since the next question may
/// be fine. It is made fresh for each pass and holds nothing between them.
/// </summary>
public sealed class BoundedTasteJudge(ITasteJudge inner, int most) : ITasteJudge
{
    private TasteJudgement.Unavailable? stopped;

    /// <summary>How many questions were forwarded to the judge.</summary>
    public int Asked { get; private set; }

    /// <summary>Whether the judge said it has no key to ask with.</summary>
    public bool SawNoKey { get; private set; }

    public async Task<TasteJudgement> JudgeAsync(TasteEvidence evidence, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (stopped is not null)
        {
            return new TasteJudgement.Unavailable(stopped.Reason, "Not asked again this pass: " + (stopped.Detail ?? "the judge could not answer."));
        }

        if (Asked >= most)
        {
            return new TasteJudgement.Unavailable(AuthorUnavailable.Offline, "Left for the next pass: this pass has asked its limit.");
        }

        Asked++;
        var judgement = await inner.JudgeAsync(evidence, cancellation);
        if (judgement is TasteJudgement.Unavailable { Reason: AuthorUnavailable.NotConfigured or AuthorUnavailable.KeyRejected or AuthorUnavailable.Offline or AuthorUnavailable.TimedOut } unavailable)
        {
            stopped = unavailable;
            SawNoKey = unavailable.Reason == AuthorUnavailable.NotConfigured;
        }

        return judgement;
    }
}
