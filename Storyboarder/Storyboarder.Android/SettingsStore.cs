namespace Storyboarder.Android;

// App settings persisted in MAUI Preferences (Android SharedPreferences). Read live by the
// background worker via the LiveOptions wrappers in MauiProgram, so edits take effect without a
// restart. API keys are entered on the Settings page; Google access uses the device account.
public static class SettingsStore
{
    /// <summary>
    /// The stored keys, frozen as literals. They were <c>nameof(...)</c> — tidy-looking, and a trap:
    /// renaming a property renames its key, and the stored value is not migrated, not reported, simply
    /// not found. The values below are the old property names for exactly that reason, so no phone
    /// notices the rename. Guarded by FolioInvariantsTests.
    /// </summary>
    internal static class Keys
    {
        internal const string ClaudeApiKey = "ClaudeApiKey";
        internal const string GeminiApiKey = "GeminiApiKey";
        internal const string VaultPassphrase = "VaultPassphrase";
        internal const string ArchiveToDrive = "ArchiveToDrive";
        internal const string FreeLocalCopiesAfterArchive = "FreeLocalCopiesAfterArchive";
        internal const string DeliverToRemarkable = "DeliverToRemarkable";
        internal const string RemarkableDeviceToken = "RemarkableDeviceToken";
        internal const string GoogleAccountEmail = "GoogleAccountEmail";
        internal const string LastRunOutcome = "LastRunOutcome";
        internal const string LastRunAtUtc = "LastRunAtUtc";
        internal const string LastMigrationError = "LastMigrationError";

        // The pivot's one-time boolean. Still read, never written: it is how a device that already
        // dropped its database once is recognised as generation 1.
        internal const string PivotSchemaReset = "CanonizerSchemaReset";
        internal const string SchemaResetGeneration = "SchemaResetGeneration";
    }

    public static string ClaudeApiKey
    {
        get => Preferences.Get(Keys.ClaudeApiKey, string.Empty);
        set => Preferences.Set(Keys.ClaudeApiKey, value);
    }

    public static string GeminiApiKey
    {
        get => Preferences.Get(Keys.GeminiApiKey, string.Empty);
        set => Preferences.Set(Keys.GeminiApiKey, value);
    }

    // The passphrase that encrypts the shared Drive secrets vault (Storyworthtelling.Voice.Secrets). It is
    // the one credential deliberately NOT kept in the vault — it is wiped by a factory reset and re-entered
    // once here, which then unlocks restoring every other key from Drive. Empty means this app doesn't yet
    // participate in the vault (it keeps working from the keys above).
    public static string VaultPassphrase
    {
        get => Preferences.Get(Keys.VaultPassphrase, string.Empty);
        set => Preferences.Set(Keys.VaultPassphrase, value);
    }

    // Copy each finished book's PDFs to a Drive folder beside its manuscript. Off until the drive.file
    // write scope is registered on the OAuth client and consented to on the device — turning it on before
    // then just sends every finished book through a stage that can only fail.
    public static bool ArchiveToDrive
    {
        get => Preferences.Get(Keys.ArchiveToDrive, false);
        set => Preferences.Set(Keys.ArchiveToDrive, value);
    }

    // After a confirmed upload, delete the intermediate panel and page images (hundreds of megabytes for a
    // full book). The PDFs themselves are always kept — they are what gets read.
    public static bool FreeLocalCopiesAfterArchive
    {
        get => Preferences.Get(Keys.FreeLocalCopiesAfterArchive, false);
        set => Preferences.Set(Keys.FreeLocalCopiesAfterArchive, value);
    }

    // Also deliver the finished report to a "Canonizer" folder on the owner's reMarkable tablet. Off until
    // the device is paired (RemarkableDeviceToken set); turning it on before then just sends every review
    // through a stage that can only fail.
    public static bool DeliverToRemarkable
    {
        get => Preferences.Get(Keys.DeliverToRemarkable, false);
        set => Preferences.Set(Keys.DeliverToRemarkable, value);
    }

    // The long-lived reMarkable device token from the one-time device-code pairing. Backed up to the shared
    // secrets vault (RemarkableSecrets) so a factory reset restores it without re-pairing, and shared with
    // the other apps that deliver to the same tablet.
    public static string RemarkableDeviceToken
    {
        get => Preferences.Get(Keys.RemarkableDeviceToken, string.Empty);
        set => Preferences.Set(Keys.RemarkableDeviceToken, value);
    }

    // The Google account chosen via the native picker; tokens are minted for it via AccountManager.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(Keys.GoogleAccountEmail, string.Empty);
        set => Preferences.Set(Keys.GoogleAccountEmail, value);
    }

    public static string LastRunOutcome
    {
        get => Preferences.Get(Keys.LastRunOutcome, string.Empty);
        set => Preferences.Set(Keys.LastRunOutcome, value);
    }

    public static DateTime LastRunAtUtc
    {
        get => Preferences.Get(Keys.LastRunAtUtc, DateTime.MinValue);
        set => Preferences.Set(Keys.LastRunAtUtc, value);
    }

    // Set when applying EF migrations at process start fails, cleared on success. Surfaced on the
    // Settings page so a broken update is visible instead of silently stalling the worker.
    public static string LastMigrationError
    {
        get => Preferences.Get(Keys.LastMigrationError, string.Empty);
        set => Preferences.Set(Keys.LastMigrationError, value);
    }

    // The schema reset is a GENERATION, not a flag, and this is why: the pivot dropped the database once,
    // gated by a boolean, and it is already true on the one device that matters. P10 drops it again — a
    // fresh start, by decision — so reusing that boolean would mean the second drop never happens there,
    // leaving a database stamped with migration ids the new baseline cannot build on and a Migrate() that
    // fails on a phone nobody can debug. Storing the generation makes the next reset a number instead of
    // another key, and the key itself carries no brand name so the next rename need not touch it.
    public const int CurrentSchemaResetGeneration = 2;

    public static int SchemaResetGeneration
    {
        // A device that did the pivot's reset reads back as generation 1 rather than as "never reset",
        // so the ladder continues from where that device actually is.
        get => Preferences.Get(Keys.SchemaResetGeneration, 0) is var stored && stored > 0
            ? stored
            : Preferences.Get(Keys.PivotSchemaReset, false) ? 1 : 0;
        set => Preferences.Set(Keys.SchemaResetGeneration, value);
    }

    public static bool SchemaResetIsDue => SchemaResetGeneration < CurrentSchemaResetGeneration;

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
