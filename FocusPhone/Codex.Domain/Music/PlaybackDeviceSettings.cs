namespace Codex.Domain.Music;

/// <summary>Playback preferences remembered for one output device (a car's Bluetooth, headphones, the speaker).</summary>
public sealed record DevicePlaybackSetting(string DeviceKey, int VolumePercent, string? EqualizerPreset, float Speed);

/// <summary>
/// Per-output-device memory: the volume / EQ preset / speed to restore when a given device connects, so the
/// car comes back loud and the headphones quiet without fiddling. Keyed by a stable device id/name the
/// Android side supplies; an unknown device resolves to a neutral default. Pure.
/// </summary>
public sealed class PlaybackDeviceSettings
{
    private readonly Dictionary<string, DevicePlaybackSetting> byKey;

    public PlaybackDeviceSettings(IEnumerable<DevicePlaybackSetting>? saved = null) =>
        byKey = saved?.ToDictionary(setting => setting.DeviceKey) ?? [];

    public DevicePlaybackSetting Resolve(string deviceKey) =>
        byKey.TryGetValue(deviceKey, out var setting)
            ? setting
            : new DevicePlaybackSetting(deviceKey, VolumePercent: 100, EqualizerPreset: null, Speed: 1.0f);

    public void Set(DevicePlaybackSetting setting) => byKey[setting.DeviceKey] = setting;

    public IReadOnlyList<DevicePlaybackSetting> Export() => [.. byKey.Values];
}
