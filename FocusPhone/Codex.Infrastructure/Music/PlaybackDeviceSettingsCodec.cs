using System.Text.Json;
using Codex.Domain.Music;

namespace Codex.Infrastructure.Music;

/// <summary>Serializes the per-device playback settings for the preferences store; garbled/absent → empty.</summary>
public static class PlaybackDeviceSettingsCodec
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { PropertyNameCaseInsensitive = true };

    public static string Encode(IReadOnlyList<DevicePlaybackSetting> settings) =>
        JsonSerializer.Serialize(settings, SerializerOptions);

    public static IReadOnlyList<DevicePlaybackSetting> Decode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<DevicePlaybackSetting>>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
