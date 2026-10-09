using Pulp.Domain.Core;

namespace Pulp.Domain.People;

/// <summary>One person in the household, and whether they are a child.</summary>
public sealed record HouseholdViewer(ViewerId Id, bool IsChild);

/// <summary>
/// The household's viewers, and the one place "is this viewer a child?" is answered (D23, D283).
/// Taste, the Friday stack and anything else that treats a child differently ask here rather than
/// carry their own flag or set, because two sources drift and the drift is silent: a stale set of
/// children lets a family sitting train an adult's taste. A viewer the roster does not name is
/// refused, never taken for an adult.
/// </summary>
public sealed class HouseholdRoster
{
    private readonly Dictionary<ViewerId, HouseholdViewer> viewers = [];

    public HouseholdRoster(IEnumerable<HouseholdViewer> viewers)
    {
        ArgumentNullException.ThrowIfNull(viewers);
        foreach (var viewer in viewers)
        {
            ArgumentNullException.ThrowIfNull(viewer);
            if (!this.viewers.TryAdd(viewer.Id, viewer))
            {
                throw new ArgumentException(
                    $"'{viewer.Id}' is on the roster twice; a viewer has one answer to whether they are a child.",
                    nameof(viewers));
            }
        }

        if (this.viewers.Count == 0)
        {
            throw new ArgumentException("A household roster needs at least one viewer.", nameof(viewers));
        }

        Viewers = this.viewers.Values.ToList();
        Children = this.viewers.Values.Where(viewer => viewer.IsChild).Select(viewer => viewer.Id).ToHashSet();
    }

    public IReadOnlyList<HouseholdViewer> Viewers { get; }

    /// <summary>Every child on the roster.</summary>
    public IReadOnlySet<ViewerId> Children { get; }

    public bool Contains(ViewerId viewer) => viewers.ContainsKey(viewer);

    /// <summary>Whether <paramref name="viewer"/> is a child; a viewer not on the roster is refused.</summary>
    public bool IsChild(ViewerId viewer) =>
        viewers.TryGetValue(viewer, out var entry)
            ? entry.IsChild
            : throw new ArgumentException(
                $"'{viewer}' is not on the household roster; an unknown viewer is never taken for an adult.",
                nameof(viewer));
}
