using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Shell;

namespace Pulp.Domain.ChildView;

/// <summary>
/// The areas a viewer's binder shows. A child sees three tabs — Watching, Playlists, Discover (D231);
/// no Sports, Calendar or Settings in a child's view, so the Settings group's Household and Vault
/// entries are not reachable from it either.
/// </summary>
public static class ChildViewAreas
{
    public static IReadOnlyList<PulpArea> All { get; } = [PulpArea.Watching, PulpArea.Playlists, PulpArea.Discover];

    public static bool Shows(PulpArea area) => All.Contains(area);

    /// <summary>The binder's areas, in strip order: every area for an adult, the child's three in a child's view.</summary>
    public static IReadOnlyList<PulpArea> For(ChildViewSession? session) =>
        session is null ? PulpAreas.All.Select(spec => spec.Area).ToList() : session.Areas;
}

/// <summary>
/// "Watch as a child" (D213): one child, or several together (D175, D280), and the area the adult
/// left from, which "Back to me" restores. A session holds the children by id only; what they may see
/// is read from the household each time (<see cref="Resolve"/>), so a parent's change to a filter
/// applies at once rather than to the next session. On the phone, leaving needs no PIN (D43).
/// </summary>
public sealed record ChildViewSession
{
    public ChildViewSession(IReadOnlyCollection<ViewerId> children, PulpArea returnTo)
    {
        ArgumentNullException.ThrowIfNull(children);
        if (children.Count == 0)
        {
            throw new ArgumentException("A child's view needs a child; with nobody picked there is nobody to be strict for.", nameof(children));
        }

        if (!Enum.IsDefined(returnTo))
        {
            throw new ArgumentOutOfRangeException(nameof(returnTo), returnTo, "Back to me needs an area to return to.");
        }

        Children = children.Distinct().ToList();
        ReturnTo = returnTo;
    }

    /// <summary>Who is watching, in the order they were picked.</summary>
    public IReadOnlyList<ViewerId> Children { get; }

    /// <summary>The adult's area when the child's view began.</summary>
    public PulpArea ReturnTo { get; }

    public IReadOnlyList<PulpArea> Areas => ChildViewAreas.All;

    public bool Shows(PulpArea area) => ChildViewAreas.Shows(area);

    /// <summary>
    /// Where a request for <paramref name="requested"/> lands in this view: the area itself when the
    /// child sees it, else the child's home. A sub-page's binder (the title page draws all six tabs)
    /// asks by area, so this is the one place a Settings tab tapped there is turned away.
    /// </summary>
    public PulpArea Route(PulpArea requested) => Shows(requested) ? requested : PulpArea.Watching;

    /// <summary>What "Back to me" restores: the device owner's view, on the area they left from.</summary>
    public PulpArea BackToMe() => ReturnTo;

    /// <summary>The line under the header (D179), naming the children by <paramref name="nameOf"/>.</summary>
    public string Label(Func<ViewerId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(nameOf);
        return ChildViewText.WatchingAs(Children.Select(nameOf).ToList());
    }

    /// <summary>
    /// The children's own filters, read from the household as it stands. Every child picked must still
    /// be a child on the roster with rules of their own; otherwise there is no audience and the page
    /// says why — a child judged by nothing would be shown everything.
    /// </summary>
    public ChildAudienceResult Resolve(HouseholdRoster? roster, IEnumerable<ChildRules> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (roster is null)
        {
            return new ChildAudienceResult(null, ChildViewText.NoHousehold);
        }

        var known = rules.GroupBy(rule => rule.Child).ToDictionary(group => group.Key, group => group.First());
        var picked = new List<ChildRules>();
        foreach (var child in Children)
        {
            if (!roster.Contains(child) || !roster.IsChild(child))
            {
                return new ChildAudienceResult(null, ChildViewText.NotAChild(child.Value));
            }

            if (!known.TryGetValue(child, out var rule))
            {
                return new ChildAudienceResult(null, ChildViewText.NoFilter(child.Value));
            }

            picked.Add(rule);
        }

        return new ChildAudienceResult(ChildAudience.Of(picked), null);
    }
}

/// <summary>A resolved child's view: the audience, or the quiet sentence saying why there is none. Never voiced.</summary>
public sealed record ChildAudienceResult(ChildAudience? Audience, string? Problem);

/// <summary>
/// Keeps a child's view across the shell going away, so a phone handed to a child does not come back
/// as the adult's after Android reclaims the app. One line for the area to return to, then one child
/// id a line. Anything it cannot read is no session.
/// </summary>
public static class ChildViewSessionCodec
{
    public static string Encode(ChildViewSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return string.Join('\n', new[] { AreaRequest.Word(session.ReturnTo) }.Concat(session.Children.Select(child => child.Value)));
    }

    public static ChildViewSession? Decode(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var lines = text.Split('\n');
        if (AreaRequest.Read(lines[0]) is not { } returnTo)
        {
            return null;
        }

        var children = lines.Skip(1).Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => new ViewerId(line.Trim())).ToList();
        return children.Count == 0 ? null : new ChildViewSession(children, returnTo);
    }
}

/// <summary>One way to start a child's view: a child, or the children together (D175).</summary>
public sealed record ChildPick(string Label, IReadOnlyList<ViewerId> Children);

/// <summary>
/// What "Watch as a child" offers (D213, D280): each child on their own, then all of them together
/// when there are several. With one child there is one choice, and the shell takes it without asking.
/// </summary>
public static class ChildPickChoices
{
    public static IReadOnlyList<ChildPick> For(IReadOnlyList<ViewerId> children, Func<ViewerId, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(nameOf);
        var distinct = children.Distinct().ToList();
        var choices = distinct.Select(child => new ChildPick(nameOf(child), [child])).ToList();
        if (distinct.Count > 1)
        {
            choices.Add(new ChildPick(ChildViewText.Together(distinct.Select(nameOf).ToList()), distinct));
        }

        return choices;
    }

    /// <summary>The roster's children, in the roster's order; none without a roster.</summary>
    public static IReadOnlyList<ViewerId> ChildrenOf(HouseholdRoster? roster) =>
        roster?.Viewers.Where(viewer => viewer.IsChild).Select(viewer => viewer.Id).ToList() ?? [];
}
