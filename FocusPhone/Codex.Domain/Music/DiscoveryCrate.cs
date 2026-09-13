namespace Codex.Domain.Music;

/// <summary>
/// One record in the crate, in the form the screen shows it: what it is called, who
/// made it, and the sleeve to draw.
///
/// Deliberately not a <see cref="Track"/>. Today the crate is filled from the owner's
/// own library and tomorrow from a streaming catalogue, and the screen should not
/// have to change when that changes — a pick is a thing with a name, a maker and a
/// sleeve either way.
/// </summary>
public sealed record CratePick(
    string Id, string Title, string Subtitle, string? ArtRef = null, string? Why = null,
    DiscoveryKind Kind = DiscoveryKind.Song);

/// <summary>
/// The week's crate: one record showing, the rest of the run behind it, and whatever
/// has been struck out.
///
/// Stateful rather than a value, like <see cref="Codex.Domain.Design.Selection"/>,
/// because it is the screen's working state — the view moves through it as chevrons
/// are tapped.
///
/// The rule that shapes it: <b>a veto is struck, not deleted.</b> The record stays in
/// the run wearing an editor's mark, and stepping back lifts it. That is the whole
/// reason the crate runs backwards as well as forwards — without <i>Bring it back</i>
/// a mis-tap would be final, and a crate you cannot undo is one nobody uses honestly.
/// </summary>
public sealed class DiscoveryCrate
{
    private readonly List<CratePick> picks;
    private readonly List<string> vetoed = [];

    public DiscoveryCrate(IReadOnlyList<CratePick> picks, IEnumerable<string>? alreadyVetoed = null)
    {
        this.picks = [.. picks];

        // Strikes are restored, not trusted: a stack is replaced every Friday, so a
        // veto held over from a run that is gone must not colour this one.
        if (alreadyVetoed is not null)
        {
            vetoed.AddRange(alreadyVetoed.Where(id => this.picks.Any(pick => pick.Id == id)).Distinct());
        }
    }

    public IReadOnlyList<CratePick> Picks => picks;

    public IReadOnlyList<string> Vetoed => vetoed;

    public int Index { get; private set; }

    public bool IsEmpty => picks.Count == 0;

    public CratePick? Current => IsEmpty ? null : picks[Index];

    /// <summary>How much of the run is still to come — the stepped sleeve edges.</summary>
    public int Behind => IsEmpty ? 0 : picks.Count - Index - 1;

    public bool IsVetoed(string id) => vetoed.Contains(id);

    public bool CurrentIsVetoed => Current is { } pick && IsVetoed(pick.Id);

    /// <summary>
    /// Forward and back <b>clamp</b> rather than wrap. A crate is a stack you flick
    /// through, so it has a front and a back; wrapping would make "how much is left"
    /// — the thing the stepped edges are showing — a lie.
    /// </summary>
    public void Next()
    {
        if (!IsEmpty && Index < picks.Count - 1)
        {
            Index++;
        }
    }

    public void Previous()
    {
        if (Index > 0)
        {
            Index--;
        }
    }

    /// <summary>Strike the current record and move on: passing on it is a reason to
    /// see the next one.</summary>
    public void Veto()
    {
        if (Current is not { } pick)
        {
            return;
        }

        if (!vetoed.Contains(pick.Id))
        {
            vetoed.Add(pick.Id);
        }

        Next();
    }

    public void Restore()
    {
        if (Current is { } pick)
        {
            vetoed.Remove(pick.Id);
        }
    }
}

/// <summary>
/// How the crate is drawn behind the record you are looking at.
///
/// Three edges and no more: the point of the stepped sleeves is that the stack has
/// visible depth and is seen to shrink, and past three the steps stop being countable
/// and become texture. The number left is stated beside the chevrons, which is what
/// actually reports the size of the run.
/// </summary>
public static class CrateGeometry
{
    public const int MaxEdges = 3;

    /// <summary>How far each edge peeks out from the one in front of it.</summary>
    public const int StepDp = 7;

    public static int VisibleEdges(int behind) => Math.Clamp(behind, 0, MaxEdges);
}
