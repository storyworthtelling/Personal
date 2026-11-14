namespace Codex.Domain.Music;

/// <summary>What kind of output an audio device is, as far as remembering settings for it goes.</summary>
public enum AudioOutputKind
{
    Speaker,
    Wired,
    Usb,
    Bluetooth,
    Other,
}

/// <summary>One output the phone could be playing through, by kind and the product name it reports.</summary>
public sealed record AudioOutput(AudioOutputKind Kind, string Name);

/// <summary>
/// Which output music is actually going to, and the key its settings are remembered under. A connected
/// Bluetooth device wins over a USB or wired headset, which wins over the phone's speaker — the order Android
/// routes media in. Keyed by kind and name, so a car and a pair of headphones are two devices and the same
/// headphones are one device each time they connect.
/// </summary>
public static class DeviceAudioRoute
{
    private static readonly AudioOutputKind[] Priority =
        [AudioOutputKind.Bluetooth, AudioOutputKind.Usb, AudioOutputKind.Wired, AudioOutputKind.Speaker];

    public static string? KeyFor(IEnumerable<AudioOutput> outputs)
    {
        var list = outputs.ToList();
        foreach (var kind in Priority)
        {
            var output = list.FirstOrDefault(candidate => candidate.Kind == kind);
            if (output is null)
            {
                continue;
            }
            return kind == AudioOutputKind.Speaker
                ? "speaker"
                : $"{kind.ToString().ToLowerInvariant()}:{output.Name.Trim()}";
        }
        return null;
    }
}

/// <summary>
/// The per-device memory over <see cref="PlaybackDeviceSettings"/>: when a device becomes the route, what it
/// was last set to comes back; while it is the route, what the owner sets is learned for it. Nothing is
/// learned until a route is known, so the first reading after start cannot be filed under the wrong device.
/// </summary>
public sealed class DeviceAudioMemory(PlaybackDeviceSettings settings)
{
    public string? Current { get; private set; }

    /// <summary>The route moved to <paramref name="deviceKey"/>: the settings to restore, or null for a device never seen.</summary>
    public DevicePlaybackSetting? Routed(string deviceKey)
    {
        Current = deviceKey;
        return settings.Export().FirstOrDefault(setting => setting.DeviceKey == deviceKey);
    }

    /// <summary>What the current device is set to now. True when that is new for it and should be saved.</summary>
    public bool Observe(int volumePercent, string? equalizerPreset)
    {
        if (Current is null)
        {
            return false;
        }

        var next = new DevicePlaybackSetting(Current, volumePercent, equalizerPreset);
        var known = settings.Export().FirstOrDefault(setting => setting.DeviceKey == Current);
        if (known == next)
        {
            return false;
        }
        settings.Set(next);
        return true;
    }
}
