namespace DailyLoader.Android;

public static class CredentialStore
{
    public static string NytSessionCookie
    {
        get => Preferences.Get(nameof(NytSessionCookie), string.Empty);
        set => Preferences.Set(nameof(NytSessionCookie), value);
    }

    public static string NytApiKey
    {
        get => Preferences.Get(nameof(NytApiKey), string.Empty);
        set => Preferences.Set(nameof(NytApiKey), value);
    }

    public static string RemarkableDeviceToken
    {
        get => Preferences.Get(nameof(RemarkableDeviceToken), string.Empty);
        set => Preferences.Set(nameof(RemarkableDeviceToken), value);
    }

    // The Google account chosen via the native "Sign in with Google" picker. Access tokens are
    // obtained from Google Play Services for this account, so no client id/secret is stored.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(nameof(GoogleAccountEmail), string.Empty);
        set => Preferences.Set(nameof(GoogleAccountEmail), value);
    }

    // Anthropic API key for handwriting transcription (optional; read-back is skipped without it).
    public static string AnthropicApiKey
    {
        get => Preferences.Get(nameof(AnthropicApiKey), string.Empty);
        set => Preferences.Set(nameof(AnthropicApiKey), value);
    }

    // JSON for the calendar-to-column mapping (CalendarMappingOptions shape). Empty = defaults.
    public static string CalendarMappingJson
    {
        get => Preferences.Get(nameof(CalendarMappingJson), string.Empty);
        set => Preferences.Set(nameof(CalendarMappingJson), value);
    }

    public static string LastDeliveryOutcome
    {
        get => Preferences.Get(nameof(LastDeliveryOutcome), string.Empty);
        set => Preferences.Set(nameof(LastDeliveryOutcome), value);
    }

    public static DateTime LastDeliveryAtUtc
    {
        get => Preferences.Get(nameof(LastDeliveryAtUtc), DateTime.MinValue);
        set => Preferences.Set(nameof(LastDeliveryAtUtc), value);
    }

    // "Delivered — 2h ago" style summary for the home screen (DESIGN.md §11).
    public static string LastDeliverySummary =>
        string.IsNullOrEmpty(LastDeliveryOutcome) || LastDeliveryAtUtc == DateTime.MinValue
            ? "Never delivered"
            : $"{LastDeliveryOutcome} — {DailyLoader.Domain.Time.RelativeTime.Format(LastDeliveryAtUtc.ToLocalTime(), DateTime.Now)}";

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(NytSessionCookie) &&
        !string.IsNullOrEmpty(NytApiKey) &&
        !string.IsNullOrEmpty(RemarkableDeviceToken);

    // The calendar/checklist delivery runs only once Google is connected; until then the
    // existing crossword delivery is unaffected.
    public static bool IsGoogleConfigured =>
        !string.IsNullOrEmpty(GoogleAccountEmail);
}
