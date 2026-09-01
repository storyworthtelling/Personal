using System.Text.Json;

namespace Codex.Infrastructure.Music;

/// <summary>Serializes the album→image-path override map for the preferences store; garbled/absent → empty.</summary>
public static class ArtworkOverrideCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Encode(IReadOnlyDictionary<string, string> overrides) =>
        JsonSerializer.Serialize(overrides, SerializerOptions);

    public static IReadOnlyDictionary<string, string> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, SerializerOptions)
                ?? new Dictionary<string, string>();
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
