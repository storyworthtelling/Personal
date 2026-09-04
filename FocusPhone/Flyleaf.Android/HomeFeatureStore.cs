using Android.Content;
using Codex.Domain.Apps;
using Codex.Infrastructure.Apps;

namespace Flyleaf;

/// <summary>
/// Persists which apps are featured cover-out on the home shelf and in what slot order (the tray's
/// "Feature on home" action swaps one). A thin SharedPreferences adapter over
/// <see cref="HomeFeatureSetCodec"/>; an absent store loads the approved default six. Vault-backed
/// for restore. The preferences file name ("homeFeatures") is a storage key, kept stable.
/// </summary>
public class HomeFeatureStore
{
    private const string PreferencesName = "homeFeatures";
    private const string SetKey = "featured"; // JSON array of package ids in slot order

    private readonly ISharedPreferences preferences;

    public HomeFeatureStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public HomeFeatureSet Load() => HomeFeatureSetCodec.Decode(preferences.GetString(SetKey, null));

    public void Save(HomeFeatureSet featured) =>
        preferences.Edit()!.PutString(SetKey, HomeFeatureSetCodec.Encode(featured))!.Apply();
}
