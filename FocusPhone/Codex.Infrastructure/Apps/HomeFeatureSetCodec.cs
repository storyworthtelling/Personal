using System.Text.Json;
using Codex.Domain.Apps;

namespace Codex.Infrastructure.Apps;

/// <summary>
/// JSON encode/decode for the home's featured-app set, so the Android store stays a thin
/// adapter and the (de)serialization is unit-tested without a device. Stored as a flat array
/// of package ids in slot order. An absent, garbled, or empty payload decodes to
/// <see cref="HomeFeatureSet.Default"/> — the home shelf is never blank.
/// </summary>
public static class HomeFeatureSetCodec
{
    public static string Encode(HomeFeatureSet featured) =>
        JsonSerializer.Serialize(featured.Packages);

    public static HomeFeatureSet Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return HomeFeatureSet.Default;
        }

        try
        {
            var packages = JsonSerializer.Deserialize<List<string>>(json);
            return packages is null or { Count: 0 }
                ? HomeFeatureSet.Default
                : new HomeFeatureSet(packages);
        }
        catch (JsonException)
        {
            return HomeFeatureSet.Default;
        }
    }
}
