using System.Text.Json;
using FocusPhone.Domain.Audio;

namespace FocusPhone.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for the local episode cache, so the Android SharedPreferences
/// store stays a thin adapter and the (de)serialization is unit-tested without a
/// device. An absent or garbled payload decodes to <see cref="PodcastCache.Empty"/>
/// so a first run — or a corrupt cache — simply falls back to a live fetch.
/// </summary>
public static class PodcastCacheCodec
{
    public static string Encode(PodcastCache cache) => JsonSerializer.Serialize(cache);

    public static PodcastCache Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return PodcastCache.Empty;
        }

        try
        {
            return JsonSerializer.Deserialize<PodcastCache>(json) ?? PodcastCache.Empty;
        }
        catch (JsonException)
        {
            return PodcastCache.Empty;
        }
    }
}
