namespace Ledger.Android;

// App settings persisted in MAUI Preferences (Android SharedPreferences). Read live by the background
// worker via the LiveOptions wrappers in MauiProgram, so edits take effect without a restart. The
// Plaid client id/secret are entered on the Settings page; per-institution access tokens live in the
// database (see Ledger.Domain.Model.Institution), not here.
public static class SettingsStore
{
    public static string PlaidClientId
    {
        get => Preferences.Get(nameof(PlaidClientId), string.Empty);
        set => Preferences.Set(nameof(PlaidClientId), value);
    }

    public static string PlaidSecret
    {
        get => Preferences.Get(nameof(PlaidSecret), string.Empty);
        set => Preferences.Set(nameof(PlaidSecret), value);
    }

    // "sandbox" (fake data, for setup/testing) or "production" (real institutions).
    public static string PlaidEnvironment
    {
        get => Preferences.Get(nameof(PlaidEnvironment), "sandbox");
        set => Preferences.Set(nameof(PlaidEnvironment), value);
    }

    public static string LastRunOutcome
    {
        get => Preferences.Get(nameof(LastRunOutcome), string.Empty);
        set => Preferences.Set(nameof(LastRunOutcome), value);
    }

    public static DateTime LastRunAtUtc
    {
        get => Preferences.Get(nameof(LastRunAtUtc), DateTime.MinValue);
        set => Preferences.Set(nameof(LastRunAtUtc), value);
    }

    // The Google account chosen for reading the shared owner's-voice profile from Drive (the voiced
    // greeting). Empty until the owner signs in on the Settings page; empty simply means the greeting
    // stays neutral.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(nameof(GoogleAccountEmail), string.Empty);
        set => Preferences.Set(nameof(GoogleAccountEmail), value);
    }

    // Set when applying EF migrations at process start fails, cleared on success. Surfaced on the
    // Settings page so a broken update is visible instead of silently stalling the worker.
    public static string LastMigrationError
    {
        get => Preferences.Get(nameof(LastMigrationError), string.Empty);
        set => Preferences.Set(nameof(LastMigrationError), value);
    }

    // "Refreshed — 2h ago" style summary for the account-list footer (DESIGN.md §11).
    public static string LastRunSummary =>
        string.IsNullOrEmpty(LastRunOutcome) || LastRunAtUtc == DateTime.MinValue
            ? "Not refreshed yet"
            : $"{LastRunOutcome} — {Ledger.Domain.Time.RelativeTime.Format(LastRunAtUtc.ToLocalTime(), DateTime.Now)}";

    // The worker can't call Plaid until both the client id and secret are entered, so it no-ops until
    // they're set.
    public static bool IsConfigured =>
        !string.IsNullOrEmpty(PlaidClientId) && !string.IsNullOrEmpty(PlaidSecret);
}
