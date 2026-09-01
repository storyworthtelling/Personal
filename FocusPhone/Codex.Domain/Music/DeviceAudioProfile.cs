namespace Codex.Domain.Music;

/// <summary>Remembered volume + EQ preset for one output device (a car, a pair of headphones).</summary>
public record DeviceAudioProfile(string DeviceId, float Volume, string EqPreset);

/// <summary>
/// Per-output-device audio profiles: the app recalls the right volume/EQ when you
/// connect to your car versus your headphones. Pure lookup state; the Android
/// layer supplies the current device id when the audio route changes.
/// </summary>
public class DeviceAudioProfileBook
{
    private readonly Dictionary<string, DeviceAudioProfile> profiles;

    public DeviceAudioProfileBook(IEnumerable<DeviceAudioProfile>? saved = null) =>
        profiles = saved is null ? [] : saved.ToDictionary(profile => profile.DeviceId);

    public void Remember(DeviceAudioProfile profile) => profiles[profile.DeviceId] = profile;

    public DeviceAudioProfile? For(string deviceId) =>
        profiles.TryGetValue(deviceId, out var profile) ? profile : null;

    public IReadOnlyCollection<DeviceAudioProfile> Export() => [.. profiles.Values];
}
