namespace Codex.Domain.Design;

/// <summary>
/// The functional role a dog-ear notice carries (DESIGN.md §13) — the native twin of the MAUI kit's
/// <c>DogEarHue</c>, by the same names. The native fold is handed a colour rather than a role, so
/// <c>DogEar.Hues.RoleOf</c> reads a colour back to one of these.
/// </summary>
public enum FoldRole
{
    Problem,
    Caution,
    Action,
    Resolved,
    Reference,
    Progress,
}

/// <summary>
/// Which waiting notice the corner wears (D291, mockup 12): the <b>most serious</b>, not the newest.
/// The Waiting page behind the fold still lists newest first; only the fold's one choice is made here.
///
/// <para><b>The order</b>, most serious first, follows what each role means in DESIGN.md §3 and §13:
/// <b>Problem</b> (something is broken — a connection lost, a key refused), <b>Caution</b> (something is
/// going stale and wants a look soon), <b>Action</b> (a decision is waiting on the owner), <b>Progress</b>
/// (waiting, not wrong), <b>Reference</b> (only worth knowing) and <b>Resolved</b> (already settled).
/// Among notices of equal seriousness the newest wins, which is the order the corner always had.</para>
///
/// <para>A colour that is no role at all (an app's identity hue, which Post uses for an invitation)
/// claims no seriousness and ranks below every role — it still takes the corner when it is alone.</para>
///
/// <para>The MAUI kit's <c>DogEarState</c> holds the same order over <c>DogEarHue</c>, kept identical
/// by a parity test over there.</para>
/// </summary>
public static class NoticeSeriousness
{
    /// <summary>Every role, most serious first.</summary>
    public static IReadOnlyList<FoldRole> MostSeriousFirst { get; } =
    [
        FoldRole.Problem,
        FoldRole.Caution,
        FoldRole.Action,
        FoldRole.Progress,
        FoldRole.Reference,
        FoldRole.Resolved,
    ];

    /// <summary>
    /// A role's place in <see cref="MostSeriousFirst"/>: 0 is the most serious. A notice with no role
    /// ranks after every role.
    /// </summary>
    public static int Rank(FoldRole? role)
    {
        if (role is not { } known)
        {
            return MostSeriousFirst.Count;
        }

        for (var index = 0; index < MostSeriousFirst.Count; index++)
        {
            if (MostSeriousFirst[index] == known)
            {
                return index;
            }
        }

        return MostSeriousFirst.Count;
    }

    /// <summary>
    /// The index, in a newest-first list, of the notice the corner should wear: the lowest
    /// <see cref="Rank"/>, the newest on a tie. -1 when nothing is waiting.
    /// </summary>
    public static int MostSerious<T>(IReadOnlyList<T> newestFirst, Func<T, FoldRole?> roleOf)
    {
        var best = -1;
        var bestRank = int.MaxValue;
        for (var index = 0; index < newestFirst.Count; index++)
        {
            var rank = Rank(roleOf(newestFirst[index]));
            if (rank < bestRank)
            {
                best = index;
                bestRank = rank;
            }
        }

        return best;
    }
}
