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

    // Google OAuth client id (native/iOS client type, no secret) for the in-app sign-in.
    public static string GoogleClientId
    {
        get => Preferences.Get(nameof(GoogleClientId), string.Empty);
        set => Preferences.Set(nameof(GoogleClientId), value);
    }

    // Refresh token obtained from the in-app Google consent flow.
    public static string GoogleRefreshToken
    {
        get => Preferences.Get(nameof(GoogleRefreshToken), string.Empty);
        set => Preferences.Set(nameof(GoogleRefreshToken), value);
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

    public static string LastDeliveryResult
    {
        get => Preferences.Get(nameof(LastDeliveryResult), "Never delivered");
        set => Preferences.Set(nameof(LastDeliveryResult), value);
    }

    public static bool IsConfigured =>
        !string.IsNullOrEmpty(NytSessionCookie) &&
        !string.IsNullOrEmpty(NytApiKey) &&
        !string.IsNullOrEmpty(RemarkableDeviceToken);

    // The calendar/checklist delivery runs only once Google is connected; until then the
    // existing crossword delivery is unaffected.
    public static bool IsGoogleConfigured =>
        !string.IsNullOrEmpty(GoogleClientId) &&
        !string.IsNullOrEmpty(GoogleRefreshToken);
}
