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
