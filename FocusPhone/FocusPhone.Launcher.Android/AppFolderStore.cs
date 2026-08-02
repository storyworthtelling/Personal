using System.Text.Json;
using Android.Content;
using FocusPhone.Domain.Apps;

namespace FocusPhone.Launcher;

/// <summary>
/// Persists the owner's per-app folder choices in this app's preferences as a
/// package → folder-name map. Auto-sort is recomputed on every load, so only the
/// overrides need to survive: an entry here is the owner having filed an app into
/// a folder (or explicitly loosened it), and it wins over auto-placement.
/// </summary>
public class AppFolderStore
{
    private const string PreferencesName = "appFolders";
    private const string OverridesKey = "overrides"; // JSON object: package -> AppFolder name

    private readonly ISharedPreferences preferences;

    public AppFolderStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    /// <summary>The stored overrides, dropping any whose folder name no longer parses.</summary>
    public IReadOnlyDictionary<string, AppFolder> Load()
    {
        var result = new Dictionary<string, AppFolder>();
        foreach (var (package, name) in LoadRaw())
        {
            if (AppFolders.FromName(name) is { } folder)
            {
                result[package] = folder;
            }
        }
        return result;
    }

    /// <summary>Files a package into a folder (overriding auto-sort).</summary>
    public void SetFolder(string packageName, AppFolder folder)
    {
        var map = LoadRaw();
        map[packageName] = folder.ToString();
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
