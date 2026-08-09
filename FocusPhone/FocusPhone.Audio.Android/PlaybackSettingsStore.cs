using Android.Content;
using FocusPhone.Domain.Audio;
using FocusPhone.Infrastructure.Podcasts;

namespace FocusPhone.Audio;

/// <summary>
/// Persists the podcast playback settings (global default + per-feed overrides)
/// in SharedPreferences. Thin adapter over the unit-tested
/// <see cref="PlaybackSettingsCodec"/>.
/// </summary>
public class PlaybackSettingsStore
{
    private const string PreferencesName = "podcastSettings";
    private const string SettingsKey = "settings";

    private readonly ISharedPreferences preferences;

    public PlaybackSettingsStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public PlaybackSettingsSnapshot Load() =>
        PlaybackSettingsCodec.Decode(preferences.GetString(SettingsKey, null));

    public void Save(PlaybackSettingsSnapshot snapshot) =>
        preferences.Edit()!.PutString(SettingsKey, PlaybackSettingsCodec.Encode(snapshot))!.Apply();
}
