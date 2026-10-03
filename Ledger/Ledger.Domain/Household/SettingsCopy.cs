namespace Index.Domain.Household;

/// <summary>
/// The Settings words that differ between the owner's install and hers. Mockup 11 gives her Settings its
/// own copy — "Vault", a helper about the other phone, a "Signed in" line for the Google account — and
/// says nothing of Plaid, the vault's keys, or a factory reset, because her install never talks to Plaid
/// (D09). They live here, once, so the page holds no second copy and a test can read every sentence her
/// screen can show.
///
/// <para>The owner's sentences are exactly what they were before task 2.7. Mockup 10 words them
/// differently ("Vault", "The one secret not kept in the vault…"); that drift is Phase 4's, not this
/// task's.</para>
/// </summary>
public static class SettingsCopy
{
    public static string VaultHeading(InstallRole role) =>
        role == InstallRole.Hers ? "Vault" : "Secrets vault";

    /// <summary>The paragraph above the passphrase field. Her screen has none: the label and the field
    /// are the whole of it, with the helper underneath.</summary>
    public static string? VaultIntro(InstallRole role) => role == InstallRole.Hers
        ? null
        : "A passphrase encrypts one shared file in your Google Drive that holds every app's keys — here, your Plaid credentials and each connected bank's access token. Set the same passphrase in each app; after a factory reset or reinstall, sign in with Google below and your banks restore automatically. Needs the Google sign-in below.";

    /// <summary>The sentence under the passphrase field.</summary>
    public static string PassphraseNote(InstallRole role) => role == InstallRole.Hers
        ? "The same passphrase as on the other phone. It opens the balances Index shares with this one."
        : "Shown in the clear so you can read it back and copy it into your other apps — it's the one thing not stored in the vault.";

    /// <summary>The paragraph under the Google account heading. Her screen has none.</summary>
    public static string? GoogleIntro(InstallRole role) => role == InstallRole.Hers
        ? null
        : "Sign in with Google to back up your keys to the secrets vault and to greet you in your own voice from your shared Drive profile. Skip it to keep neutral copy and local-only settings.";

    /// <summary>The subhead over her signed-in account line.</summary>
    public const string SignedInHeading = "Signed in";

    /// <summary>"[account address] · Drive, where the shared balances and the voice are read" (mockup 11).</summary>
    public static string SignedInLine(string email) =>
        $"{email} · Drive, where the shared balances and the voice are read";

    /// <summary>Her sign-in button is for being signed out; once signed in, the line above replaces it.
    /// The owner's button stays as it was.</summary>
    public static bool ShowsSignIn(InstallRole role, bool signedIn) => role != InstallRole.Hers || !signedIn;
}
