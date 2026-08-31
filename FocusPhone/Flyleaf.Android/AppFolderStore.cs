using System.Text.Json;
using Android.Content;

namespace Flyleaf;

/// <summary>
/// Persists the owner's per-app shelf choices as a package → category-id map. Auto-sort is
/// recomputed on every load, so only the overrides need to survive: an entry here is the owner
/// having filed an app onto a shelf (or explicitly loosened it), and it wins over auto-placement.
/// The stored ids are <see cref="Codex.Domain.Apps.Category"/> ids; the built-in ids are the old
/// AppFolder enum names, so a device that stored "Finance" keeps resolving with no migration.
/// The preferences file name ("appFolders") is kept stable through the Codex/Flyleaf rename for
/// the same reason. An id that no longer names a category in the catalog simply falls loose.
/// </summary>
public class AppFolderStore
{
    private const string PreferencesName = "appFolders";
    private const string OverridesKey = "overrides"; // JSON object: package -> category id

    private readonly ISharedPreferences preferences;

    public AppFolderStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    /// <summary>The stored overrides (package → category id). The catalog decides which ids still exist.</summary>
    public IReadOnlyDictionary<string, string> Load() => LoadRaw();

    /// <summary>Files a package onto a shelf (overriding auto-sort).</summary>
    public void SetFolder(string packageName, string categoryId)
    {
        var map = LoadRaw();
        map[packageName] = categoryId;
        Save(map);
    }

    /// <summary>Removes any override so the package returns to auto-sort (loose unless auto-placed).</summary>
    public void ClearFolder(string packageName)
    {
        var map = LoadRaw();
        if (map.Remove(packageName))
        {
            Save(map);
        }
    }

    private Dictionary<string, string> LoadRaw()
    {
        var json = preferences.GetString(OverridesKey, null);
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }
        return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
    }

    private void Save(Dictionary<string, string> map) =>
        preferences.Edit()!.PutString(OverridesKey, JsonSerializer.Serialize(map))!.Apply();
}
