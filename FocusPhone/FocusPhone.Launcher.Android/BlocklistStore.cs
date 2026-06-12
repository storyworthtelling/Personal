using Android.Content;
using FocusPhone.Domain.Apps;

namespace FocusPhone.Launcher;

/// <summary>
/// Persists the categorizer's block/allow lists in this app's preferences.
/// The DPC cannot be uninstalled, so a block here outlives any reinstall of
/// the blocked package.
/// </summary>
public class BlocklistStore
{
    private const string PreferencesName = "blocklist";
    private const string BlockedKey = "blockedPackages";
    private const string AllowedKey = "allowedPackages";
    private const string DebuggingLockedDownKey = "debuggingLockedDown";

    private readonly ISharedPreferences preferences;

    public BlocklistStore(Context context)
    {
        preferences = context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
    }

    public CategorizerData Load() => new(
        preferences.GetStringSet(BlockedKey, [])!.ToList(),
        preferences.GetStringSet(AllowedKey, [])!.ToList());

    public void Save(CategorizerData data)
    {
        preferences.Edit()!
            .PutStringSet(BlockedKey, data.BlockedPackages.ToList())!
            .PutStringSet(AllowedKey, data.AllowedPackages.ToList())!
            .Apply();
    }

    public bool DebuggingLockedDown
    {
        get => preferences.GetBoolean(DebuggingLockedDownKey, false);
        set => preferences.Edit()!.PutBoolean(DebuggingLockedDownKey, value)!.Apply();
    }
}
