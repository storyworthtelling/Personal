namespace Codex.Domain.Post;

/// <summary>
/// One baseline line for an empty Mailbox. Most seeds carry a single rung; a seed with
/// several is a <b>ladder</b>, and each drawing of it steps up one.
/// </summary>
public sealed record EmptySeed(string Id, IReadOnlyList<string> Rungs);

/// <summary>
/// What the pool remembers between drawings: how far up each ladder the owner has
/// climbed, and which seeds are sitting out a retirement.
/// </summary>
public sealed record EmptyMailboxState(
    IReadOnlyDictionary<string, int> Rungs,
    IReadOnlyDictionary<string, DateTimeOffset> RetiredUntilUtc)
{
    public static EmptyMailboxState Fresh { get; } =
        new(new Dictionary<string, int>(), new Dictionary<string, DateTimeOffset>());
}

/// <summary>The line to show, and the state to carry into the next drawing.</summary>
public sealed record EmptyMailboxDraw(string Line, EmptyMailboxState Next);

/// <summary>
/// The empty Mailbox — the <b>only</b> seat the owner's voice has in Post. Everything
/// else, including every error, stays static-neutral: §14 permits voiced error copy but
/// nothing in the suite has ever exercised it, and now that errors sit behind the fold
/// a wry line about a dead bridge would arrive as a <i>reward</i> for turning the corner.
///
/// A line is drawn from a pool so an empty desk does not always say the same thing;
/// the voice then rewrites whatever was drawn. The raw baseline IS the fallback when
/// there is no profile or key, which is why the pool has to vary on its own.
///
/// One seed carries a <b>ladder</b>. What it counts is views of an <b>unchanged</b>
/// empty Mailbox — the compulsive checking, not the absence of mail — so real mail
/// arriving starts the count over: dealing with something and coming back to an empty
/// desk is an achievement, not a tic. Past its last rung the seed retires for about a
/// month and returns reset, because the alternative is its sharpest line becoming
/// wallpaper.
///
/// Pure: the caller supplies the clock and the roll, so what the owner sees is
/// decided and tested here rather than on a device.
/// </summary>
public static class EmptyMailbox
{
    /// <summary>How long a worn-out ladder sits out before coming back reset.</summary>
    public const int RetirementDays = 30;

    /// <summary>
    /// The baselines. Three carry the owner's wit, four are plain, and one is the
    /// ladder — the only place in the suite where an app tells the owner to go away.
    ///
    /// A fourth voiced seed the owner wrote is deliberately absent: "Don't you usually
    /// text ___ around this time? They are waiting in your Rolodex." It needs contact
    /// frequency bucketed by time of day, which is a signal Post does not gather. It is
    /// a data question wearing a copy question's clothes, and it comes back when the
    /// signal exists rather than being faked from whoever is nearest the top.
    /// </summary>
    public static IReadOnlyList<EmptySeed> Seeds { get; } =
    [
        new("cleared", ["Oh good, you finally cleared up the clutter. It was getting hard to focus around here."]),
        new("important", ["I hope you didn't delete anything important."]),
        new("waiting", ["Nothing is waiting."]),
        new("caught-up", ["You are caught up."]),
        new("desk-clear", ["The desk is clear."]),
        new("no-post", ["No post today."]),
        new("go-away",
        [
            "Hey, maybe go use a different app. I'll notify you if anything comes up here.",
            "Still nothing. I did say I would notify you.",
            "You are back again. There is still nothing. This one is on you.",
            "…",
        ]),
    ];

    /// <summary>The seeds not currently sitting out a retirement.</summary>
    public static IEnumerable<EmptySeed> Available(EmptyMailboxState state, DateTimeOffset now) =>
        Seeds.Where(seed =>
            !state.RetiredUntilUtc.TryGetValue(seed.Id, out var until) || now >= until);

    public static EmptyMailboxDraw Draw(
        EmptyMailboxState state, bool mailArrivedSinceLastView, DateTimeOffset now, int roll)
    {
        var rungs = new Dictionary<string, int>(state.Rungs);
        var retired = new Dictionary<string, DateTimeOffset>(state.RetiredUntilUtc);

        // Mail arriving restarts every climb, but it is not a pardon: a seed that just
        // wore out stays out for its month rather than being brought back by the next
        // thing to land in the Mailbox.
        if (mailArrivedSinceLastView)
        {
            rungs.Clear();
        }

        var available = Available(new EmptyMailboxState(rungs, retired), now).ToList();
        if (available.Count == 0)
        {
            // Only ladders ever retire and the plain seeds outnumber them, so this is
            // unreachable today — but a pool that cannot answer is worse than a dull
            // one, so fall back to the whole set rather than to nothing.
            available = [.. Seeds];
        }

        // The roll is whatever the caller had to hand, so it is normalized rather than
        // trusted: a negative one must not index backwards off the end of the pool.
        var chosen = available[(int)(((uint)roll) % (uint)available.Count)];
        var rung = rungs.TryGetValue(chosen.Id, out var climbed) ? climbed : 0;
        rung = Math.Clamp(rung, 0, chosen.Rungs.Count - 1);
        var line = chosen.Rungs[rung];

        if (chosen.Rungs.Count == 1)
        {
            // A plain seed has nowhere to climb to; drawing it changes nothing.
            return new EmptyMailboxDraw(line, new EmptyMailboxState(rungs, retired));
        }

        if (rung + 1 >= chosen.Rungs.Count)
        {
            rungs[chosen.Id] = 0;
            retired[chosen.Id] = now.AddDays(RetirementDays);
        }
        else
        {
            rungs[chosen.Id] = rung + 1;
        }

        return new EmptyMailboxDraw(line, new EmptyMailboxState(rungs, retired));
    }
}
