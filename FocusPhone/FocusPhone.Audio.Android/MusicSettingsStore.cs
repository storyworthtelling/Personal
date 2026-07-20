using Android.Content;

namespace FocusPhone.Audio;

/// <summary>
/// The music side's small settings: the signed-in Google account email and the
/// chosen Drive music-folder id. Static SharedPreferences via the application
/// context, so the token provider (which has no Activity) can read the account.
/// </summary>
public static class MusicSettingsStore
{
    private const string PreferencesName = "music";
    private const string AccountKey = "googleAccountEmail";
    private const string FolderKey = "musicFolderId";

    private static ISharedPreferences Preferences =>
        global::Android.App.Application.Context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;

    public static string? GoogleAccountEmail
    {
        get => Preferences.GetString(AccountKey, null);
        set => Preferences.Edit()!.PutString(AccountKey, value)!.Apply();
    }

    public static string? MusicFolderId
    {
        get => Preferences.GetString(FolderKey, null);
        set => Preferences.Edit()!.PutString(FolderKey, value)!.Apply();
    }
}
