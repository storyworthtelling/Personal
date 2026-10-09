using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.People;
using Pulp.Domain.Services;
using Pulp.Domain.Watching;

namespace Pulp.Domain.HandOffs;

/// <summary>
/// The one place a hand-off is decided (finding §2.4, G13). <see cref="HandOff.For"/> is public and
/// knows nothing about children, so the screens never call it: they hand a <see cref="HandOffRequest"/>
/// to the Android executor, which asks here. Anyone on the sofa who is a child — alone, beside an
/// adult, or with other children — sends the whole request through <see cref="ChildHandOff"/>, which
/// refuses before any intent exists; only a request with no child in it is built directly. Who is a
/// child is the <see cref="HouseholdRoster"/>'s answer and nobody else's (D23, D283).
/// </summary>
public sealed class HandOffPlanner
{
    private readonly HouseholdRoster roster;
    private readonly Dictionary<ViewerId, ChildRules> rules = [];
    private readonly ServiceCatalogue catalogue;
    private readonly AttestedServices attested;

    public HandOffPlanner(
        HouseholdRoster roster,
        IEnumerable<ChildRules> childRules,
        ServiceCatalogue catalogue,
        AttestedServices attested)
    {
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(childRules);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(attested);

        foreach (var child in childRules)
        {
            ArgumentNullException.ThrowIfNull(child);
            if (!roster.Contains(child.Child) || !roster.IsChild(child.Child))
            {
                throw new ArgumentException(
                    $"There are child rules for '{child.Child}', whom the roster does not name as a child; the roster alone says who is a child.",
                    nameof(childRules));
            }

            if (!rules.TryAdd(child.Child, child))
            {
                throw new ArgumentException($"'{child.Child}' has two sets of child rules.", nameof(childRules));
            }
        }

        var missing = roster.Children.Where(child => !rules.ContainsKey(child)).ToList();
        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"No child rules for {string.Join(", ", missing.Select(child => $"'{child}'"))}; a child with no filter would have nothing to be judged by.",
                nameof(childRules));
        }

        this.roster = roster;
        this.catalogue = catalogue;
        this.attested = attested;
    }

    /// <summary>
    /// Decides <paramref name="request"/> at <paramref name="at"/>: a refusal with no intent, or the
    /// intent plus the pending <see cref="HandedOff"/> entry it becomes once the app opens.
    /// </summary>
    public HandOffPlan Plan(HandOffRequest request, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Link.Title.Equals(request.Title.Id))
        {
            throw new ArgumentException(
                $"The link is for '{request.Link.Title}' but the request is about '{request.Title.Id}'; the viewer would be judged on one title and handed another.",
                nameof(request));
        }

        // IsChild refuses a viewer the roster does not name, so a guest is never taken for an adult.
        var children = request.Watching.Where(roster.IsChild).Select(child => rules[child]).ToList();

        if (children.Count == 0)
        {
            var entry = catalogue.ById(request.Service);
            if (entry is null)
            {
                return new HandOffPlan(HandOffAudience.Adult, new StartVerdict(StartRefusal.UnknownService), null, null);
            }

            return Started(HandOffAudience.Adult, new StartVerdict(StartRefusal.None), HandOff.For(request.Link, entry), request, at);
        }

        var audience = children.Count == 1 ? HandOffAudience.Child : HandOffAudience.ChildrenTogether;
        var result = ChildHandOff.For(children, request.Title, request.Link, catalogue, attested);
        return result.Intent is null
            ? new HandOffPlan(audience, result.Verdict, null, null)
            : Started(audience, result.Verdict, result.Intent, request, at);
    }

    private static HandOffPlan Started(
        HandOffAudience audience,
        StartVerdict verdict,
        HandOffIntent intent,
        HandOffRequest request,
        DateTimeOffset at)
    {
        var pending = new PendingHandOff(
            request.Title.Id,
            request.Service,
            intent.Package,
            request.Watching,
            request.SessionNames,
            new HandedOff(at));
        return new HandOffPlan(audience, verdict, intent, pending);
    }
}
