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

    // The passphrase that encrypts the shared Drive secrets vault (Storyworthtelling.Voice.Secrets). It is
    // the one credential deliberately NOT kept in the vault — it is wiped by a factory reset and re-entered
    // once here, which then unlocks restoring every other key from Drive. Empty means this app doesn't yet
    // participate in the vault (it keeps working from the keys above).
    public static string VaultPassphrase
    {
        get => Preferences.Get(nameof(VaultPassphrase), string.Empty);
        set => Preferences.Set(nameof(VaultPassphrase), value);
    }

    // Copy each finished book's PDFs to a Drive folder beside its manuscript. Off until the drive.file
    // write scope is registered on the OAuth client and consented to on the device — turning it on before
    // then just sends every finished book through a stage that can only fail.
    public static bool ArchiveToDrive
    {
        get => Preferences.Get(nameof(ArchiveToDrive), false);
        set => Preferences.Set(nameof(ArchiveToDrive), value);
    }

    // After a confirmed upload, delete the intermediate panel and page images (hundreds of megabytes for a
    // full book). The PDFs themselves are always kept — they are what gets read.
    public static bool FreeLocalCopiesAfterArchive
    {
        get => Preferences.Get(nameof(FreeLocalCopiesAfterArchive), false);
        set => Preferences.Set(nameof(FreeLocalCopiesAfterArchive), value);
    }

    // The Google account chosen via the native picker; tokens are minted for it via AccountManager.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(nameof(GoogleAccountEmail), string.Empty);
        set => Preferences.Set(nameof(GoogleAccountEmail), value);
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

    // Set when applying EF migrations at process start fails, cleared on success. Surfaced on the
    // Settings page so a broken update is visible instead of silently stalling the worker.
    public static string LastMigrationError
    {
        get => Preferences.Get(nameof(LastMigrationError), string.Empty);
        set => Preferences.Set(nameof(LastMigrationError), value);
    }

    // One-time gate for the Canonizer schema reset. The pivot collapsed the whole migration history into a
    // single new baseline, so a device upgraded in place still holds the old storyboarder.db stamped with
    // migration ids the new baseline doesn't know — Migrate() would try to create tables that already exist
    // and fail. The brief accepts no old data preserved, so the migration runner drops the legacy database
    // once (EnsureDeleted) before the first Migrate, then sets this so it never runs again.
    public static bool CanonizerSchemaReset
    {
        get => Preferences.Get(nameof(CanonizerSchemaReset), false);
        set => Preferences.Set(nameof(CanonizerSchemaReset), value);
    }

    // "Delivered — 2h ago" style summary for the library footer (DESIGN.md §11).
    public static string LastRunSummary =>
        string.IsNullOrEmpty(LastRunOutcome) || LastRunAtUtc == DateTime.MinValue
            ? "Never run"
            : $"{LastRunOutcome} — {Storyboarder.Domain.Time.RelativeTime.Format(LastRunAtUtc.ToLocalTime(), DateTime.Now)}";

    // The pipeline can't do anything without both API keys, so the worker no-ops until they're set.
    public static bool IsConfigured =>
        !string.IsNullOrEmpty(ClaudeApiKey) && !string.IsNullOrEmpty(GeminiApiKey);

    public static bool IsGoogleConfigured => !string.IsNullOrEmpty(GoogleAccountEmail);
}
