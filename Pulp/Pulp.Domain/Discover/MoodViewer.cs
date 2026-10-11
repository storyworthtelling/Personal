using Pulp.Domain.Children;
using Pulp.Domain.Core;

namespace Pulp.Domain.Discover;

/// <summary>
/// Who a mood search or a made playlist is for: one adult, one child, or a sitting with children in
/// it. <see cref="Children"/> is every child whose filter applies, and the strictest wins (D175), so a
/// sitting with a child sees only what that child may see. Never sent anywhere; it decides what is
/// shown and what may be asked, nothing more.
/// </summary>
public sealed record MoodViewer
{
    private MoodViewer(ViewerId viewer, IReadOnlyList<ChildRules> children)
    {
        Viewer = viewer;
        Children = children;
    }

    public ViewerId Viewer { get; }

    /// <summary>The children whose filter every result must pass; empty for an adult on their own.</summary>
    public IReadOnlyList<ChildRules> Children { get; }

    public bool IsFiltered => Children.Count > 0;

    public static MoodViewer Adult(ViewerId viewer) => new(viewer, []);

    public static MoodViewer Child(ChildRules child)
    {
        ArgumentNullException.ThrowIfNull(child);
        return new MoodViewer(child.Child, [child]);
    }

    /// <summary>A sitting (D174–D175): the viewer holding the phone, and the children watching with them.</summary>
    public static MoodViewer WithChildren(ViewerId viewer, IReadOnlyCollection<ChildRules> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0 || children.Any(child => child is null))
        {
            throw new ArgumentException(
                "A sitting with children needs each child's rules; with none, use Adult and say so.", nameof(children));
        }

        return new MoodViewer(viewer, children.ToList());
    }

    /// <summary>Whether the filter shows <paramref name="facts"/> to every child here; always true for an adult alone.</summary>
    public bool Shows(TitleFacts facts) => !IsFiltered || ChildFilter.DecideTogether(Children, facts).Visible;
}
