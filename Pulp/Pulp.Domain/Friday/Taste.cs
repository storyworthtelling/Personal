using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Domain.Friday;

/// <summary>
/// A title someone finished watching, with its descriptive tags and everyone who watched it. The
/// passive half of what taste is learned from (D24).
/// </summary>
public sealed record WatchedTitle(TitleId Title, IReadOnlyList<string> Tags, IReadOnlyList<ViewerId> Viewers);

/// <summary>A "Not for me" on the stack, with the tags of what was turned down.</summary>
public sealed record Veto(TitleId Title, IReadOnlyList<string> Tags);

/// <summary>
/// One viewer's vetoes. Permanent (D111): a vetoed title never comes back in a later stack unless
/// "Bring it back" lifts it. Immutable; the household file persists it.
/// </summary>
public sealed class VetoBook
{
    private readonly IReadOnlyDictionary<TitleId, Veto> vetoes;

    private VetoBook(ViewerId viewer, IReadOnlyDictionary<TitleId, Veto> vetoes)
    {
        Viewer = viewer;
        this.vetoes = vetoes;
    }

    public ViewerId Viewer { get; }

    public IReadOnlyCollection<Veto> Vetoes => vetoes.Values.ToList();

    public static VetoBook For(ViewerId viewer) => new(viewer, new Dictionary<TitleId, Veto>());

    public static VetoBook From(ViewerId viewer, IEnumerable<Veto> vetoes)
    {
        ArgumentNullException.ThrowIfNull(vetoes);
        var book = For(viewer);
        foreach (var veto in vetoes)
        {
            book = book.Veto(veto.Title, veto.Tags);
        }

        return book;
    }

    public bool IsVetoed(TitleId title) => vetoes.ContainsKey(title);

    public VetoBook Veto(TitleId title, IReadOnlyList<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return new VetoBook(Viewer, new Dictionary<TitleId, Veto>(vetoes) { [title] = new Veto(title, tags) });
    }

    /// <summary>"Bring it back": the strike lifts now and the title may come round again (D111, D220).</summary>
    public VetoBook BringBack(TitleId title)
    {
        var next = new Dictionary<TitleId, Veto>(vetoes);
        next.Remove(title);
        return new VetoBook(Viewer, next);
    }
}

/// <summary>
/// One adult's taste: a weight per descriptive tag, raised by what they watch and lowered by
/// what they veto. No ratings (D24).
/// </summary>
public sealed class TasteProfile
{
    private readonly IReadOnlyDictionary<string, int> weights;

    private TasteProfile(IReadOnlyDictionary<string, int> weights) => this.weights = weights;

    public static TasteProfile Empty { get; } = new(new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyDictionary<string, int> Weights => weights;

    public static TasteProfile From(IReadOnlyDictionary<string, int> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        return new TasteProfile(new Dictionary<string, int>(weights, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>How well a title's tags match: the sum of their weights, each tag once.</summary>
    public int Affinity(IEnumerable<string> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Sum(tag => weights.TryGetValue(tag, out var weight) ? weight : 0);
    }
}

/// <summary>
/// D24, D23 and D283: taste is learned passively from what one adult watches, plus their vetoes.
/// Each adult has their own — only the viewer's own viewing counts, never the other adult's, and
/// never a child's: anything watched with a child present is a child's viewing too and is left out.
/// A child has no profile at all. Who is a child is the <see cref="HouseholdRoster"/>'s answer alone,
/// and a viewer it does not name — the learner, or anyone in a sitting the learner was part of — is refused.
/// </summary>
public static class TasteLearner
{
    public static TasteProfile Learn(
        ViewerId viewer,
        HouseholdRoster roster,
        IEnumerable<WatchedTitle> watched,
        VetoBook vetoes)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(watched);
        ArgumentNullException.ThrowIfNull(vetoes);

        if (roster.IsChild(viewer))
        {
            return TasteProfile.Empty;
        }

        if (vetoes.Viewer != viewer)
        {
            throw new ArgumentException(
                $"The vetoes are {vetoes.Viewer}'s, not {viewer}'s; one viewer's vetoes never shape another's taste.",
                nameof(vetoes));
        }

        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var counted = new HashSet<TitleId>();
        foreach (var title in watched)
        {
            // Only a sitting that would count is asked about, and every viewer in it must be on the
            // roster: an unnamed co-viewer may be a child nobody listed, so it refuses rather than counts.
            var theirs = title.Viewers.Contains(viewer);
            if (theirs && !title.Viewers.Any(roster.IsChild) && counted.Add(title.Title))
            {
                Adjust(weights, title.Tags, 1);
            }
        }

        foreach (var veto in vetoes.Vetoes)
        {
            Adjust(weights, veto.Tags, -1);
        }

        return TasteProfile.From(weights);
    }

    private static void Adjust(Dictionary<string, int> weights, IEnumerable<string> tags, int by)
    {
        foreach (var tag in tags.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            weights[tag] = weights.GetValueOrDefault(tag) + by;
        }
    }
}
