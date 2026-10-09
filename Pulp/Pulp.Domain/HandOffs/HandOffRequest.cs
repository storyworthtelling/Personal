using Pulp.Domain.Children;
using Pulp.Domain.Core;
using Pulp.Domain.Services;

namespace Pulp.Domain.HandOffs;

/// <summary>
/// A screen's ask to hand a title off: who is watching (an adult, a child, or several people together
/// — the roster says which of them are children), what the filter knows about the title, and the
/// title's link on the chosen service, which names that service. <see cref="SessionNames"/> are the
/// names the playing app's media session might publish for it (the show, the episode), for the
/// mis-landing check (D122); empty when nothing is known.
/// </summary>
public sealed record HandOffRequest
{
    public HandOffRequest(
        IReadOnlyCollection<ViewerId> watching,
        TitleFacts title,
        HandOffLink link,
        IReadOnlyCollection<string> sessionNames)
    {
        ArgumentNullException.ThrowIfNull(watching);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(sessionNames);
        if (watching.Count == 0)
        {
            throw new ArgumentException(
                "A hand-off needs someone watching; with nobody named there is no one to judge it for, and an empty sofa is never an adult.",
                nameof(watching));
        }

        Watching = watching.Distinct().ToList();
        Title = title;
        Link = link;
        SessionNames = sessionNames.Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).ToList();
    }

    public IReadOnlyList<ViewerId> Watching { get; }

    public TitleFacts Title { get; }

    public HandOffLink Link { get; }

    public ServiceId Service => Link.Service;

    public IReadOnlyList<string> SessionNames { get; }
}
