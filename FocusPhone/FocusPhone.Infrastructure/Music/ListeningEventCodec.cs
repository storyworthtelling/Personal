using System.Text.Json;
using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// Serializes the on-device listening history to and from JSON for the preferences-backed store. A null,
/// blank, or garbled blob decodes to an empty list — a mangled or partially-written file must never strand
/// the recorder on an exception, the same way the podcast codecs treat corrupt state.
/// </summary>
public static class ListeningEventCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Encode(IReadOnlyList<ListeningEvent> events) =>
        JsonSerializer.Serialize(events, SerializerOptions);

    public static IReadOnlyList<ListeningEvent> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ListeningEvent>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
