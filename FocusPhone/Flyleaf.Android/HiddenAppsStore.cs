using Android.Content;
using Codex.Domain.Apps;

namespace Flyleaf;

/// <summary>
/// Persists the apps Pulp asked Flyleaf to hide (D92). A thin SharedPreferences adapter over
/// <see cref="HiddenAppsCodec"/>: absent or garbled loads empty. Nothing clears it when Pulp goes
/// missing or breaks, by design (D93): the streaming apps stay hidden. The preferences file name
/// is a storage key, kept stable.
/// </summary>
public class HiddenAppsStore
{
    private const string SetKey = "packages"; // JSON array of package ids

    private static int version;

    private readonly ISharedPreferences preferences;

    public HiddenAppsStore(Context context)
    {
        preferences = context.GetSharedPreferences(HiddenAppsHandoff.PreferencesName, FileCreationMode.Private)!;
    }

    /// <summary>Bumped on every <see cref="Save"/> in this process, so a reader can cache the set and reload only when it changed.</summary>
    public static int Version => Volatile.Read(ref version);

    public HiddenApps Load() => HiddenAppsCodec.Decode(preferences.GetString(SetKey, null));

    public void Save(HiddenApps hidden)
    {
        preferences.Edit()!.PutString(SetKey, HiddenAppsCodec.Encode(hidden))!.Commit();
        Interlocked.Increment(ref version);
    }
}
