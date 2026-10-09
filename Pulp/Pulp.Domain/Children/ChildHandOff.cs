using Pulp.Domain.Services;

namespace Pulp.Domain.Children;

/// <summary>A child's hand-off: the verdict, and the intent only when the verdict allows it.</summary>
public sealed record ChildHandOffResult(StartVerdict Verdict, HandOffIntent? Intent);

/// <summary>
/// The hand-off for a child, or for a group with a child in it (G13). The gate is asked first and the
/// intent is built only on a yes, so a refusal — above all an unattested service (D210) — leaves
/// nothing behind that a later step could send.
/// </summary>
public static class ChildHandOff
{
    public static ChildHandOffResult For(
        IReadOnlyCollection<ChildRules> children,
        TitleFacts title,
        HandOffLink link,
        StreamingService service,
        AttestedServices attested)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(link);
        if (!link.Title.Equals(title.Id))
        {
            throw new ArgumentException(
                $"The link is for '{link.Title}' but the filter was asked about '{title.Id}'; the child would be judged on one title and handed another.",
                nameof(link));
        }

        var verdict = ChildFilter.CanStart(children, title, service, attested);
        return verdict.MayStart
            ? new ChildHandOffResult(verdict, HandOff.For(link, service))
            : new ChildHandOffResult(verdict, null);
    }
}
