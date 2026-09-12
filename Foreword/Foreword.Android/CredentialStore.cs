using Foreword.Domain.Identity;

namespace Foreword.Android;

public static class CredentialStore
{
    // The keys are PreferenceKeys constants, never nameof(property). nameof reads as tidy and is
    // a trap: renaming a property renames its key, and nothing reports the loss — the old value
    // is simply not found and the app comes up as though it had never been configured. The
    // constants' values are the old property names, so no phone notices this change.
    public static string NytSessionCookie
    {
        get => Preferences.Get(PreferenceKeys.NytSessionCookie, string.Empty);
        set => Preferences.Set(PreferenceKeys.NytSessionCookie, value);
    }

    public static string NytApiKey
    {
        get => Preferences.Get(PreferenceKeys.NytApiKey, string.Empty);
        set => Preferences.Set(PreferenceKeys.NytApiKey, value);
    }

    public static string RemarkableDeviceToken
    {
        get => Preferences.Get(PreferenceKeys.RemarkableDeviceToken, string.Empty);
        set => Preferences.Set(PreferenceKeys.RemarkableDeviceToken, value);
    }

    // The Google account chosen via the native "Sign in with Google" picker. Access tokens are
    // obtained from Google Play Services for this account, so no client id/secret is stored.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(PreferenceKeys.GoogleAccountEmail, string.Empty);
        set => Preferences.Set(PreferenceKeys.GoogleAccountEmail, value);
    }

    // Anthropic API key for handwriting transcription (optional; read-back is skipped without it).
    public static string AnthropicApiKey
    {
        get => Preferences.Get(PreferenceKeys.AnthropicApiKey, string.Empty);
        set => Preferences.Set(PreferenceKeys.AnthropicApiKey, value);
    }

    // JSON for the calendar-to-column mapping (CalendarMappingOptions shape). Empty = defaults.
    public static string CalendarMappingJson
    {
        get => Preferences.Get(PreferenceKeys.CalendarMappingJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.CalendarMappingJson, value);
    }

    // The passphrase that encrypts the shared Drive secrets vault (Storyworthtelling.Voice.Secrets). It is
    // the one credential deliberately NOT kept in the vault — wiped by a factory reset and re-entered once
    // here, which then unlocks restoring the NYT cookie/key, the reMarkable token, and the Anthropic key
    // from Drive. Empty means this app doesn't yet participate in the vault.
    // The work shifts and the off-day words. WorkScheduleOptions was hardcoded with no screen at
    // all — Rowan 08:00–16:00, Jordan 08:00–15:30 — so changing a shift meant changing a default and
    // building the app. C1's foot edits this.
    public static string WorkScheduleJson
    {
        get => Preferences.Get(PreferenceKeys.WorkScheduleJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.WorkScheduleJson, value);
    }

    // Which calendar ids were present when the list was last opened, so a calendar shared with the
    // owner overnight can be shown as new without anything being pressed.
    public static string SeenCalendarIds
    {
        get => Preferences.Get(PreferenceKeys.SeenCalendarIds, string.Empty);
        set => Preferences.Set(PreferenceKeys.SeenCalendarIds, value);
    }

    // The time the scheduled work is actually set for, which is not always the time the app wants:
    // WorkManager keeps the schedule a phone already holds, so this is how a moved delivery time
    // reaches a phone that has run an older build.
    public static string ScheduledDeliveryTime
    {
        get => Preferences.Get(PreferenceKeys.ScheduledDeliveryTime, string.Empty);
        set => Preferences.Set(PreferenceKeys.ScheduledDeliveryTime, value);
    }

    // The Maps Platform key — one key, four APIs (routes, weather, pollen, air quality), because
    // Maps Platform bills a project rather than a product.
    public static string MapsApiKey
    {
        get => Preferences.Get(PreferenceKeys.MapsApiKey, string.Empty);
        set => Preferences.Set(PreferenceKeys.MapsApiKey, value);
    }

    // The house. As text for the router, which takes an address, and as a point for the forecast,
    // which takes coordinates.
    public static string HomeAddress
    {
        get => Preferences.Get(PreferenceKeys.HomeAddress, string.Empty);
        set => Preferences.Set(PreferenceKeys.HomeAddress, value);
    }

    public static double HomeLatitude
    {
        get => Preferences.Get(PreferenceKeys.HomeLatitude, 0d);
        set => Preferences.Set(PreferenceKeys.HomeLatitude, value);
    }

    public static double HomeLongitude
    {
        get => Preferences.Get(PreferenceKeys.HomeLongitude, 0d);
        set => Preferences.Set(PreferenceKeys.HomeLongitude, value);
    }

    // Foreword's own rules and smart tasks. Google's once tasks are NOT here — they are mirrored
    // from Google so the two can never disagree about a title or a date.
    public static string TasksJson
    {
        get => Preferences.Get(PreferenceKeys.TasksJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.TasksJson, value);
    }

    public static string AwayJson
    {
        get => Preferences.Get(PreferenceKeys.AwayJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.AwayJson, value);
    }

    public static string FeedsJson
    {
        get => Preferences.Get(PreferenceKeys.FeedsJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.FeedsJson, value);
    }

    // The defaults are the counts the brief has always used, so a phone that has never opened the
    // Sources screen keeps the paper it already had.
    public static int NewsCount
    {
        get => Preferences.Get(PreferenceKeys.NewsCount, 5);
        set => Preferences.Set(PreferenceKeys.NewsCount, value);
    }

    public static int OpinionCount
    {
        get => Preferences.Get(PreferenceKeys.OpinionCount, 3);
        set => Preferences.Set(PreferenceKeys.OpinionCount, value);
    }

    // The highest xkcd already printed. Zero means never — which the selection reads as a fresh
    // install and seeds on the latest rather than printing the archive.
    public static int SeenComic
    {
        get => Preferences.Get(PreferenceKeys.SeenComic, 0);
        set => Preferences.Set(PreferenceKeys.SeenComic, value);
    }

    public static string TasteJson
    {
        get => Preferences.Get(PreferenceKeys.TasteJson, string.Empty);
        set => Preferences.Set(PreferenceKeys.TasteJson, value);
    }

    public static string VaultPassphrase
    {
        get => Preferences.Get(PreferenceKeys.VaultPassphrase, string.Empty);
        set => Preferences.Set(PreferenceKeys.VaultPassphrase, value);
    }

    public static string LastDeliveryOutcome
    {
        get => Preferences.Get(PreferenceKeys.LastDeliveryOutcome, string.Empty);
        set => Preferences.Set(PreferenceKeys.LastDeliveryOutcome, value);
    }

    /// <summary>
    /// What happened to the prayer document. Its own line because it can fail on its own: it reads
    /// a Drive file the rest of the morning does not, behind a passphrase the rest of the morning
    /// does not need, so "Delivered" for the day and the paper while the prayer sheet never went
    /// would be one sentence saying two things.
    /// </summary>
    public static string LastPrayerOutcome
    {
        get => Preferences.Get(PreferenceKeys.LastPrayerOutcome, string.Empty);
        set => Preferences.Set(PreferenceKeys.LastPrayerOutcome, value);
    }

    public static DateTime LastDeliveryAtUtc
    {
        get => Preferences.Get(PreferenceKeys.LastDeliveryAtUtc, DateTime.MinValue);
        set => Preferences.Set(PreferenceKeys.LastDeliveryAtUtc, value);
    }

    // "Delivered — 2h ago" style summary for the home screen (DESIGN.md §11).
    public static string LastDeliverySummary =>
        string.IsNullOrEmpty(LastDeliveryOutcome) || LastDeliveryAtUtc == DateTime.MinValue
            ? "Never delivered"
            : $"{LastDeliveryOutcome} — {Foreword.Domain.Time.RelativeTime.Format(LastDeliveryAtUtc.ToLocalTime(), DateTime.Now)}";

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(NytSessionCookie) &&
        !string.IsNullOrEmpty(NytApiKey) &&
        !string.IsNullOrEmpty(RemarkableDeviceToken);

    // The calendar/checklist delivery runs only once Google is connected; until then the
    // existing crossword delivery is unaffected.
    public static bool IsGoogleConfigured =>
        !string.IsNullOrEmpty(GoogleAccountEmail);
}
