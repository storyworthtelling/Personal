using System.Text.Json;

namespace Codex.Infrastructure.Podcasts;

/// <summary>
/// JSON encode/decode for the ordered play-queue (a list of episode ids), so the
/// Android <c>QueueStore</c> stays a thin prefs adapter and the (de)serialization
/// is unit-tested. The queue is the user's ordering intent; it is persisted
/// locally only (episodes are re-derived from the feeds, like the episode cache).
/// </summary>
public static class QueueCodec
{
    public static string Encode(IReadOnlyList<string> ids) =>
        JsonSerializer.Serialize(ids);

    public static IReadOnlyList<string> Decode(string? json) =>
        string.IsNullOrEmpty(json)
            ? []
            : JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
