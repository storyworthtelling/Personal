namespace Storyboarder.Android;

// App settings persisted in MAUI Preferences (Android SharedPreferences). Read live by the
// background worker via the LiveOptions wrappers in MauiProgram, so edits take effect without a
// restart. API keys are entered on the Settings page; Google access uses the device account.
public static class SettingsStore
{
    public static string ClaudeApiKey
    {
        get => Preferences.Get(nameof(ClaudeApiKey), string.Empty);
        set => Preferences.Set(nameof(ClaudeApiKey), value);
    }

    public static string GeminiApiKey
    {
        get => Preferences.Get(nameof(GeminiApiKey), string.Empty);
        set => Preferences.Set(nameof(GeminiApiKey), value);
    }

    // The Google account chosen via the native picker; tokens are minted for it via AccountManager.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(nameof(GoogleAccountEmail), string.Empty);
        set => Preferences.Set(nameof(GoogleAccountEmail), value);
    }

    public static string LastRunResult
    {
        get => Preferences.Get(nameof(LastRunResult), "Never run");
        set => Preferences.Set(nameof(LastRunResult), value);
    }

    // The pipeline can't do anything without both API keys, so the worker no-ops until they're set.
    public static bool IsConfigured =>
        !string.IsNullOrEmpty(ClaudeApiKey) && !string.IsNullOrEmpty(GeminiApiKey);

    public static bool IsGoogleConfigured => !string.IsNullOrEmpty(GoogleAccountEmail);
}
