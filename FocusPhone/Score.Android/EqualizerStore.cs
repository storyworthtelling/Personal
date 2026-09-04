using Android.Content;
using Codex.Domain.Music;
using Codex.Infrastructure.Music;

namespace Score;

/// <summary>
/// Persists the equalizer library (active curve + saved presets) and the loudness boost, shared between the
/// band-editor screen (which edits them) and the playback service (which applies them to the audio session).
/// The device's band count is only known on device, so a first load returns null and the caller seeds a flat
/// curve sized to the hardware.
/// </summary>
public class EqualizerStore
{
    private const string PreferencesName = "equalizer";
    private const string LibraryKey = "library";
    private const string LoudnessKey = "loudnessMillibels";

    private readonly ISharedPreferences preferences;

    public EqualizerStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public EqualizerLibrary? LoadLibrary() => EqualizerCodec.Decode(preferences.GetString(LibraryKey, null));

    public void SaveLibrary(EqualizerLibrary library) =>
        preferences.Edit()!.PutString(LibraryKey, EqualizerCodec.Encode(library))!.Apply();

    /// <summary>Extra loudness applied via LoudnessEnhancer, in millibels (0 = off).</summary>
    public int LoudnessMillibels
    {
        get => preferences.GetInt(LoudnessKey, 0);
        set => preferences.Edit()!.PutInt(LoudnessKey, value)!.Apply();
    }
}
