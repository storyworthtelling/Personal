using Pulp.Domain.Core;
using Pulp.Domain.Friday;
using Pulp.Domain.People;
using Pulp.Domain.Services;

namespace Pulp.Domain.Discover;

/// <summary>
/// The household half of what a mood search or a made playlist sends: the services the household
/// has, by name, and the viewer's taste as a few liked tags. Nothing else — no title anyone watched,
/// no viewer, no child — and the types here cannot hold one (<c>DiscoverContextTests</c>).
/// <see cref="Services"/> is kept for resolving the answer and is never sent.
/// </summary>
public sealed record DiscoverContext
{
    /// <summary>Enough to steer the model; a whole profile would be the viewer's history by another name.</summary>
    public const int MaxTasteTags = 8;

    private DiscoverContext(IReadOnlyList<ServiceId> services, IReadOnlyList<string> serviceNames, IReadOnlyList<string> tasteTags, bool forChildren)
    {
        Services = services;
        ServiceNames = serviceNames;
        TasteTags = tasteTags;
        ForChildren = forChildren;
    }

    /// <summary>The household's services by id, for keeping only titles on them. Not sent.</summary>
    public IReadOnlyList<ServiceId> Services { get; }

    /// <summary>The household's services by display name, which is what the model is told.</summary>
    public IReadOnlyList<string> ServiceNames { get; }

    /// <summary>The adult viewer's most-liked tags, strongest first; empty for a child or a sitting with one.</summary>
    public IReadOnlyList<string> TasteTags { get; }

    /// <summary>
    /// Whether a child is in the view, so the model is asked for titles suitable for children. Says
    /// nothing about which child or their ceiling; the filter, not the model, is what keeps them safe.
    /// </summary>
    public bool ForChildren { get; }

    /// <summary>
    /// Builds the context for <paramref name="viewer"/>. A child has no taste (D23), and a sitting with
    /// a child is not choosing for the adult's taste, so neither sends any. Who is a child is the
    /// roster's answer alone: a child handed in without their own rules, or a viewer the roster does
    /// not name, is refused rather than taken for an adult.
    /// </summary>
    public static DiscoverContext For(
        MoodViewer viewer,
        HouseholdRoster roster,
        IEnumerable<StreamingService> householdServices,
        TasteProfile taste)
    {
        ArgumentNullException.ThrowIfNull(viewer);
        ArgumentNullException.ThrowIfNull(roster);
        ArgumentNullException.ThrowIfNull(householdServices);
        ArgumentNullException.ThrowIfNull(taste);

        if (roster.IsChild(viewer.Viewer) && !viewer.Children.Any(child => child.Child == viewer.Viewer))
        {
            throw new ArgumentException(
                $"'{viewer.Viewer}' is a child on the roster but was handed in without their filter; their search would show them everything.",
                nameof(viewer));
        }

        foreach (var child in viewer.Children)
        {
            if (!roster.IsChild(child.Child))
            {
                throw new ArgumentException(
                    $"'{child.Child}' has a child's rules but the roster does not name them a child.", nameof(viewer));
            }
        }

        var services = householdServices.DistinctBy(service => service.Id).ToList();
        var tags = viewer.IsFiltered
            ? []
            : taste.Weights
                .Where(weight => weight.Value > 0)
                .OrderByDescending(weight => weight.Value)
                .ThenBy(weight => weight.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MaxTasteTags)
                .Select(weight => weight.Key)
                .ToList();

        return new DiscoverContext(
            services.Select(service => service.Id).ToList(),
            services.Select(service => service.Name).ToList(),
            tags,
            viewer.IsFiltered);
    }
}
