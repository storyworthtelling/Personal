using Pulp.Domain.Services;

namespace Pulp.Domain.Children;

/// <summary>A child's hand-off: the verdict, and the intent only when the verdict allows it.</summary>
public sealed record ChildHandOffResult(StartVerdict Verdict, HandOffIntent? Intent);

/// <summary>
/// The hand-off for a child, or for a group with a child in it (G13). The gate is asked first and the
/// intent is built only on a yes, so a refusal — above all an unattested service (D210) — leaves
/// nothing behind that a later step could send. The service is the link's, resolved from the
/// catalogue: whether it is kids-only and which package the intent is aimed at both come from the
/// catalogue entry, never from a record the caller built.
/// </summary>
public static class ChildHandOff
{
    public static ChildHandOffResult For(
        IReadOnlyCollection<ChildRules> children,
        TitleFacts title,
        HandOffLink link,
        ServiceCatalogue catalogue,
        AttestedServices attested)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(catalogue);
        if (!link.Title.Equals(title.Id))
        {
            throw new ArgumentException(
                $"The link is for '{link.Title}' but the filter was asked about '{title.Id}'; the child would be judged on one title and handed another.",
                nameof(link));
        }

        var verdict = ChildFilter.CanStart(children, title, link.Service, catalogue, attested);
        if (!verdict.MayStart)
        {
            return new ChildHandOffResult(verdict, null);
        }

        var entry = catalogue.ById(link.Service)
            ?? throw new InvalidOperationException(
                $"The gate allowed '{link.Service}' but the catalogue has no entry for it; the gate refuses unknown services.");
        return new ChildHandOffResult(verdict, HandOff.For(link, entry));
    }
}
