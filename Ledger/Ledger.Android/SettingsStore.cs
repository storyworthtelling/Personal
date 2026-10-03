using System.Text.Json;
using Index.Domain.Household;

namespace Index.Android;

// App settings persisted in MAUI Preferences (Android SharedPreferences). Read live by the background
// worker via the LiveOptions wrappers in MauiProgram, so edits take effect without a restart. The
// Plaid client id/secret are entered on the Settings page; per-institution access tokens live in the
// database (see Index.Domain.Model.Institution), not here.
public static class SettingsStore
{
    /// <summary>
    /// The stored keys, frozen as literals. They were <c>nameof(...)</c> — tidy-looking, and a trap:
    /// renaming a property renames its key, and the stored value is not migrated, not reported, simply
    /// not found. The values below are exactly the old property names for that reason, so nothing on
    /// device notices the freeze. Guarded by IndexInvariantsTests.
    /// </summary>
    internal static class Keys
    {
        internal const string PlaidClientId = "PlaidClientId";
        internal const string PlaidSecret = "PlaidSecret";
        internal const string PlaidEnvironment = "PlaidEnvironment";
        internal const string VaultPassphrase = "VaultPassphrase";
        internal const string LastRunOutcome = "LastRunOutcome";
        internal const string LastRunAtUtc = "LastRunAtUtc";
        internal const string GoogleAccountEmail = "GoogleAccountEmail";
        internal const string LastMigrationError = "LastMigrationError";
        internal const string BudgetTrackingOnly = "BudgetTrackingOnly";
        internal const string BudgetCategories = "BudgetCategories";
        internal const string SnowballLastView = "SnowballLastView";
        internal const string SavingsLastView = "SavingsLastView";
        internal const string LastWorksheetOutcome = "LastWorksheetOutcome";
        internal const string LastWorksheetAtUtc = "LastWorksheetAtUtc";
        internal const string RemarkableDeviceToken = "RemarkableDeviceToken";
        internal const string InstallRole = "InstallRole";
        internal const string SnowballHistoryImported = "SnowballHistoryImported";
    }

    public static string PlaidClientId
    {
        get => Preferences.Get(Keys.PlaidClientId, string.Empty);
        set => Preferences.Set(Keys.PlaidClientId, value);
    }

    public static string PlaidSecret
    {
        get => Preferences.Get(Keys.PlaidSecret, string.Empty);
        set => Preferences.Set(Keys.PlaidSecret, value);
    }

    // Index talks to Plaid's production environment only (D144) — there is no environment control
    // anywhere in the app, so this no longer reads a stored Preferences value; it always answers
    // "production". Kept as a property rather than removed, because VaultSync.PublishAsync still
    // needs an authoritative value to write into the vault's wire record (PlaidSecrets.Environment
    // stays on the wire for compatibility with the old Ledger app's writes, per the plan). Nothing
    // can write a different value here any more: the setter that used to back the old environment
    // picker is gone along with the picker itself.
    public static string PlaidEnvironment => "production";

    // The passphrase that encrypts the shared Drive secrets vault (Storyworthtelling.Voice.Secrets). It is
    // the one credential deliberately NOT kept in the vault — wiped by a factory reset and re-entered once
    // here, which then unlocks restoring the Plaid credentials and every bank's access token from Drive.
    // Empty means this app doesn't yet participate in the vault.
    public static string VaultPassphrase
    {
        get => Preferences.Get(Keys.VaultPassphrase, string.Empty);
        set
        {
            // The remembered role was derived from this passphrase opening the household file.
            ForgetInstallRoleIfChanged(Keys.VaultPassphrase, value);
            Preferences.Set(Keys.VaultPassphrase, value);
        }
    }

    // Which install this is, settled once from the household file (InstallRoleService) and remembered
    // here so no page and no launch has to ask Drive again. Null means not settled yet — callers treat
    // that as Owner, InstallRoleResolver's own safe default. Cleared whenever the signed-in account or
    // the vault passphrase changes, the two inputs the answer depends on.
    public static InstallRole? CachedInstallRole
    {
        get => Preferences.Get(Keys.InstallRole, string.Empty) switch
        {
            nameof(InstallRole.Owner) => InstallRole.Owner,
            nameof(InstallRole.Hers) => InstallRole.Hers,
            _ => null,
        };
        set
        {
            if (value is { } role)
                Preferences.Set(Keys.InstallRole, role.ToString());
            else
                Preferences.Remove(Keys.InstallRole);
        }
    }

    // Set once the owner's recorded Debt Snowball history has been brought in from their Drive (or found to
    // be here already), so later opens of the Snowball page never ask Drive again. Written only on success:
    // a missing file, a missing or wrong passphrase or a failed read leaves it unset and the next open
    // tries again (SnowballService.ImportHistoryIfNeededAsync).
    public static bool SnowballHistoryImported
    {
        get => Preferences.Get(Keys.SnowballHistoryImported, false);
        set => Preferences.Set(Keys.SnowballHistoryImported, value);
    }

    private static void ForgetInstallRoleIfChanged(string key, string newValue)
    {
        if (Preferences.Get(key, string.Empty) != newValue)
            Preferences.Remove(Keys.InstallRole);
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

    // The Google account chosen for reading the shared owner's-voice profile from Drive (the voiced
    // greeting). Empty until the owner signs in on the Settings page; empty simply means the greeting
    // stays neutral.
    public static string GoogleAccountEmail
    {
        get => Preferences.Get(Keys.GoogleAccountEmail, string.Empty);
        set
        {
            // A different account may be a different person's phone: the remembered role is void.
            ForgetInstallRoleIfChanged(Keys.GoogleAccountEmail, value);
            Preferences.Set(Keys.GoogleAccountEmail, value);
        }
    }

    // Set when applying EF migrations at process start fails, cleared on success. Surfaced on the
    // Settings page so a broken update is visible instead of silently stalling the worker.
    public static string LastMigrationError
    {
        get => Preferences.Get(Keys.LastMigrationError, string.Empty);
        set => Preferences.Set(Keys.LastMigrationError, value);
    }

    // "Refreshed 2h ago" — the status line under Accounts' header (mockups 2 and 10, DESIGN.md §11). The
    // wording is Index.Domain's, tested there; this only supplies the stored outcome and the clock.
    public static string LastRunSummary => Index.Domain.Household.RefreshOnOpen.Summary(
        LastRunOutcome,
        // MinValue means "never"; converting it would shift it off the sentinel the summary tests for.
        LastRunAtUtc == DateTime.MinValue ? DateTime.MinValue : LastRunAtUtc.ToLocalTime(),
        DateTime.Now);

    // Named for the thing, switched on deliberately (D96): watch spending without limits this month,
    // never inferred automatically from an unreturned or unconfirmed worksheet.
    public static bool BudgetTrackingOnly
    {
        get => Preferences.Get(Keys.BudgetTrackingOnly, false);
        set => Preferences.Set(Keys.BudgetTrackingOnly, value);
    }

    // The six starter categories mockup-10 draws, shown on a fresh install so Settings' Budget group
    // is never empty. Release C/E's real BudgetCategory EF entity is what will let a removed category
    // keep its historical months (D55) via a soft-delete flag; this list has no months to keep yet —
    // it is local-only scaffolding for the structural add/rename/reorder/remove Settings owns (D110),
    // ahead of Budget's own schema.
    private static readonly string[] DefaultBudgetCategories =
        ["Groceries", "Dining out", "Fuel", "Household", "Kids", "Subscriptions"];

    public static IReadOnlyList<string> BudgetCategories
    {
        get
        {
            var stored = Preferences.Get(Keys.BudgetCategories, string.Empty);
            if (string.IsNullOrEmpty(stored))
                return DefaultBudgetCategories;

            try
            {
                return JsonSerializer.Deserialize<List<string>>(stored) ?? [];
            }
            catch (JsonException)
            {
                // A corrupt blob is treated as "nothing known yet" — the app still works, it just
                // shows the same starter categories a fresh install would.
                return DefaultBudgetCategories;
            }
        }
        set => Preferences.Set(Keys.BudgetCategories, JsonSerializer.Serialize(value));
    }

    // D139: the Loans | History segmented control remembers the last side chosen, across visits —
    // mockup 6's own note ("Snowball opens on Loans; the switch remembers the last side you chose").
    public static string SnowballLastView
    {
        get => Preferences.Get(Keys.SnowballLastView, "Loans");
        set => Preferences.Set(Keys.SnowballLastView, value);
    }

    // D139's own pattern, applied to Savings' identical Accounts|Growth switch (mockup 9).
    public static string SavingsLastView
    {
        get => Preferences.Get(Keys.SavingsLastView, "Accounts");
        set => Preferences.Set(Keys.SavingsLastView, value);
    }

    // Release F (D89): the monthly worksheet round-trip is silent to the owner — no toast, no
    // notification — so this is the one place a silent failure becomes visible instead of a mystery,
    // the same idiom as LastRunSummary below for the balance refresh.
    public static string LastWorksheetOutcome
    {
        get => Preferences.Get(Keys.LastWorksheetOutcome, string.Empty);
        set => Preferences.Set(Keys.LastWorksheetOutcome, value);
    }

    public static DateTime LastWorksheetAtUtc
    {
        get => Preferences.Get(Keys.LastWorksheetAtUtc, DateTime.MinValue);
        set => Preferences.Set(Keys.LastWorksheetAtUtc, value);
    }

    public static string LastWorksheetSummary =>
        string.IsNullOrEmpty(LastWorksheetOutcome) || LastWorksheetAtUtc == DateTime.MinValue
            ? "No worksheet delivered yet"
            : $"{LastWorksheetOutcome} — {Index.Domain.Time.RelativeTime.Format(LastWorksheetAtUtc.ToLocalTime(), DateTime.Now)}";

    // The reMarkable's long-lived device token (Storyworthtelling.Remarkable.RemarkableOptions),
    // backed up to and restored from the shared secrets vault's RemarkableSecrets.DeviceToken — the
    // same shared field Foreword's own pairing writes, so pairing once in either app lets the other
    // restore it (see CLAUDE.md's "reMarkable delivery (cross-cutting)"). Index does not pair itself
    // (no device-code UI here); this exists so a token Foreword already paired can reach Index's own
    // worksheet delivery through VaultSync.RestoreAsync.
    public static string RemarkableDeviceToken
    {
        get => Preferences.Get(Keys.RemarkableDeviceToken, string.Empty);
        set => Preferences.Set(Keys.RemarkableDeviceToken, value);
    }

    // The worker can't call Plaid until both the client id and secret are entered, so it no-ops until
    // they're set.
    public static bool IsConfigured =>
        !string.IsNullOrEmpty(PlaidClientId) && !string.IsNullOrEmpty(PlaidSecret);
}
