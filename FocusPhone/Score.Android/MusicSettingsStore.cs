using System.Globalization;
using Android.Content;
using Codex.Domain.Music;

namespace Score;

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
    private const string StackSizeKey = "fridayStackSize";
    private const string TempoGapKey = "tempoGap";
    private const string LastGreetingDateKey = "lastGreetingLocalDate";

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

    /// <summary>
    /// How many records the Friday crate holds. Clamped on the way out as well as in,
    /// so a value written by an older build (or a future one) can never hand the
    /// worker a stack size the owner could not have chosen.
    /// </summary>
    public static int FridayStackSize
    {
        get => FridayStack.Clamp(Preferences.GetInt(StackSizeKey, FridayStack.Default));
        set => Preferences.Edit()!.PutInt(StackSizeKey, FridayStack.Clamp(value))!.Apply();
    }

    /// <summary>
    /// What the workout could not find enough of — "more tracks near 165 BPM".
    ///
    /// A measurement rather than a preference, which is what lets it cross into the
    /// weekly crate without breaking the promise that a workout teaches the taste
    /// profile nothing. Friday reads it and clears it, so a tempo tried twice does not
    /// stack up into a demand.
    /// </summary>
    public static string? TempoGap
    {
        get => Preferences.GetString(TempoGapKey, null);
        set => Preferences.Edit()!.PutString(TempoGapKey, value)!.Apply();
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

    /// <summary>
    /// The owner's-local calendar date the owner's-voice greeting last actually displayed
    /// something (not merely attempted — see <see cref="Codex.Domain.Music.FirstOpenOfDay"/>),
    /// so <c>ScoreActivity.ShowGreetingAsync</c> knows whether this open is the day's first —
    /// the one the nightly rescan's findings ride in on.
    /// </summary>
    public static DateOnly? LastGreetingLocalDate
    {
        get
        {
            var stored = Preferences.GetString(LastGreetingDateKey, null);
            return stored is not null && DateOnly.TryParseExact(
                stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? date
                : null;
        }
        set => Preferences.Edit()!
            .PutString(LastGreetingDateKey, value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))!
            .Apply();
    }
}
