using System.Collections.Immutable;
using Pulp.Domain.Core;

namespace Pulp.Domain.Availability;

/// <summary>
/// The per-title homes the viewer chose with "open there instead" (D216). Immutable: choosing again
/// returns a new set, and the latest choice for a title stands.
/// </summary>
public sealed class HomeOverrides
{
    public static readonly HomeOverrides None = new(ImmutableDictionary<TitleId, ServiceId>.Empty);

    private readonly ImmutableDictionary<TitleId, ServiceId> chosen;

    private HomeOverrides(ImmutableDictionary<TitleId, ServiceId> chosen) => this.chosen = chosen;

    public IReadOnlyDictionary<TitleId, ServiceId> Chosen => chosen;

    public HomeOverrides OpenThereInstead(TitleId title, ServiceId service) => new(chosen.SetItem(title, service));

    public ServiceId? For(TitleId title) => chosen.TryGetValue(title, out var service) ? service : null;
}
