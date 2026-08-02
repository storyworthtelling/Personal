using Android.Content;
using FocusPhone.Domain.Music;
using FocusPhone.Infrastructure.Music;

namespace FocusPhone.Audio;

/// <summary>Persists the album → custom-art-URI map (via the tested codec) in SharedPreferences.</summary>
public class ArtworkOverrideStore
{
    private const string PreferencesName = "artwork";
    private const string OverridesKey = "overrides";

    private readonly ISharedPreferences preferences;

    public ArtworkOverrideStore(Context context) =>
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    private ArtworkOverrides Load() =>
        new(ArtworkOverrideCodec.Decode(preferences.GetString(OverridesKey, null)));

    public string? Resolve(string albumKey) => Load().Resolve(albumKey);

    public void Set(string albumKey, string imageUri)
    {
        var overrides = Load();
        overrides.Set(albumKey, imageUri);
        Save(overrides);
    }

    private void Save(ArtworkOverrides overrides) =>
        preferences.Edit()!.PutString(OverridesKey, ArtworkOverrideCodec.Encode(overrides.Export()))!.Apply();
}
