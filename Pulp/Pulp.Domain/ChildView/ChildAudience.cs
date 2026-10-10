using Pulp.Domain.Availability;
using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Discover;
using Pulp.Domain.Services;

namespace Pulp.Domain.ChildView;

/// <summary>
/// Who a child's view is for, as every area reads it. What it may show is the strictest child's answer
/// read as an intersection (D175, D280): a title is shown only when every child watching would be shown
/// it, so one child's allow-list never opens a title to another. A hand-off names the children and
/// nobody else (G13), so the gate judges them and the sitting is theirs, never an adult's (D23).
/// </summary>
public sealed class ChildAudience
{
    private ChildAudience(IReadOnlyList<ChildRules> children)
    {
        Children = children;
        Watching = children.Select(child => child.Child).ToList();
    }

    public IReadOnlyList<ChildRules> Children { get; }

    /// <summary>Who a hand-off is for, and whose sitting it records.</summary>
    public IReadOnlyList<ViewerId> Watching { get; }

    /// <summary>The child whose age band sets the voice's reading level (D233): the youngest band watching.</summary>
    public ChildRules ReadingLevel => Children.OrderBy(child => child.Ceiling).First();

    /// <summary>A mood search for these children (D290): their filters, and no taste (D23).</summary>
    public MoodViewer Mood =>
        Children.Count == 1 ? MoodViewer.Child(Children[0]) : MoodViewer.WithChildren(Children[0].Child, Children);

    public static ChildAudience Of(IReadOnlyCollection<ChildRules> children)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0 || children.Any(child => child is null))
        {
            throw new ArgumentException("A child's audience needs each child's rules; with none it would have nobody to be strict for.", nameof(children));
        }

        return new ChildAudience(children.DistinctBy(child => child.Child).ToList());
    }

    /// <summary>Whether every child watching may see the title. A title with no facts is hidden, as unrated is (D128).</summary>
    public bool Shows(TitleFacts? facts) => facts is not null && ChildFilter.DecideTogether(Children, facts).Visible;

    /// <summary>The items whose titles every child may see, in their order.</summary>
    public IReadOnlyList<T> Visible<T>(IEnumerable<T> items, Func<T, TitleFacts?> factsOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(factsOf);
        return items.Where(item => Shows(factsOf(item))).ToList();
    }

    /// <summary>
    /// The home Play asks for: the best home every child may start on (D210, D282), else the best home
    /// all the same, so the gate refuses it and the dog-ear says why rather than Play doing nothing.
    /// </summary>
    public Home? HomeFor(HomeChoice homes, TitleFacts title, ServiceCatalogue catalogue, AttestedServices attested)
    {
        ArgumentNullException.ThrowIfNull(homes);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(attested);
        if (homes.Best is not { } best)
        {
            return null;
        }

        return new[] { best }.Concat(homes.Others)
            .FirstOrDefault(home => ChildFilter.CanStart(Children, title, home.Service, catalogue, attested).MayStart)
            ?? best;
    }
}
