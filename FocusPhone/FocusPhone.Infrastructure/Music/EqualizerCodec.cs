using System.Text.Json;
using FocusPhone.Domain.Music;

namespace FocusPhone.Infrastructure.Music;

/// <summary>
/// Serializes the equalizer library (active curve + saved presets) for the preferences-backed store. A
/// null, blank, or garbled blob decodes to null so the caller can fall back to a flat curve sized to the
/// device's band count (which only the device knows).
/// </summary>
public static class EqualizerCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Encode(EqualizerLibrary library) => JsonSerializer.Serialize(library, SerializerOptions);

    public static EqualizerLibrary? Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<EqualizerLibrary>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
