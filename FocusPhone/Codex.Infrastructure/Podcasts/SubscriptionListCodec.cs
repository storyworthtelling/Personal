using System.Text.Json;
using Codex.Domain.Audio;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for the ordered, titled subscription list, so the Android
/// store stays a thin adapter and the (de)serialization is unit-tested. Replaces
/// the old unordered feed-URL string set.
/// </summary>
public static class SubscriptionListCodec
{
    public static string Encode(IReadOnlyList<PodcastSubscription> subscriptions) =>
        JsonSerializer.Serialize(subscriptions);

    public static IReadOnlyList<PodcastSubscription> Decode(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<PodcastSubscription>>(json) ?? [];
}
