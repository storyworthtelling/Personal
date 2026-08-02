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
    private const string LearningKey = "listeningGeniusEnabled";
    private const string LastRefreshKey = "tasteLastRefreshUtc";

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

    /// <summary>Listening Genius kill switch. On by default — the owner asked for the feature; off stops
    /// all passive learning and the recorder derives nothing.</summary>
    public static bool ListeningGeniusEnabled
    {
        get => Preferences.GetBoolean(LearningKey, true);
        set => Preferences.Edit()!.PutBoolean(LearningKey, value)!.Apply();
    }

    /// <summary>When the Taste Profile was last refreshed, so the app throttles the Claude refine.</summary>
    public static DateTimeOffset? TasteLastRefreshUtc
    {
        get
        {
            var ticks = Preferences.GetLong(LastRefreshKey, 0);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
        set => Preferences.Edit()!.PutLong(LastRefreshKey, value?.UtcTicks ?? 0)!.Apply();
    }
}
