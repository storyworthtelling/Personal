using System.Text.Json;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>
/// JSON encode/decode for the owner's saved TIDAL tracks — the list
/// <c>MusicLibrary.Streamed()</c> reads to fold streaming tracks into the merged
/// catalog (see <c>LibraryMerge</c>), so the (de)serialization is unit-tested rather
/// than living inside the Android store. Garbled input decodes to empty rather than
/// throwing, the same rule every other small persisted list in this app follows —
/// a corrupt SharedPreferences value should look like an empty save list, not crash
/// the catalog load it feeds.
/// </summary>
public static class SavedTidalTracksCodec
{
    public static string Encode(IReadOnlyList<Track> tracks) => JsonSerializer.Serialize(tracks);

    public static IReadOnlyList<Track> Decode(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<Track>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
