using Pulp.Domain.Actions;

namespace Pulp.Domain.FirstRun;

/// <summary>What came of reading the vault, in Pulp's words (the head maps the vault's own status onto it).</summary>
public enum VaultRestoreOutcome
{
    NotSignedIn,
    NoPassphrase,

    /// <summary>The passphrase and account are there but Drive holds no vault yet: the first install to save makes it.</summary>
    NoVaultYet,

    Opened,
    WrongPassphrase,
    Unreadable,

    /// <summary>Drive could not be reached, or the read threw.</summary>
    Failed,
}

/// <summary>What came of saving Pulp's section to the vault.</summary>
public enum VaultSaveOutcome
{
    Saved,
    NotSignedIn,
    NoPassphrase,

    /// <summary>This install reads the household vault and does not write it — the design, not a fault.</summary>
    ReadOnly,

    Unreadable,
    WriteFailed,
}

/// <summary>
/// What the Vault page and first run say after a restore or a save. A success is an inline sentence
/// (the state change, DESIGN.md §5 — no toast); a failure is a dog-ear notice (D260) and never a
/// sentence on the page, never voiced (§14). Every outcome is exactly one of the two. All share one
/// key, so the next attempt replaces the notice and a success withdraws it.
/// </summary>
public static class VaultOutcomes
{
    public const string NoticeKey = "vault";

    public const string SignInKey = "vault:sign-in";

    private const string KeysLiveInTheVault =
        "Pulp keeps its keys only in the vault, so they are not stored until the vault can be written.";

    public static string? Sentence(VaultRestoreOutcome outcome, int keysPresent) => outcome switch
    {
        VaultRestoreOutcome.NotSignedIn => "Sign in with Google so Pulp can find the vault in Drive.",
        VaultRestoreOutcome.NoPassphrase => "Enter the vault passphrase to bring Pulp's keys back.",
        VaultRestoreOutcome.NoVaultYet => "There is no vault in Drive yet. Add the keys in Vault and Save to make one.",
        VaultRestoreOutcome.Opened when keysPresent >= FirstRunPlan.KeyCount => "The vault is open, and all three keys came with it.",
        VaultRestoreOutcome.Opened => $"The vault is open; {keysPresent} of the three keys came with it. Add the others in Vault.",
        _ => null,
    };

    public static string? Sentence(VaultSaveOutcome outcome) => outcome switch
    {
        VaultSaveOutcome.Saved => "Saved to the vault.",
        VaultSaveOutcome.ReadOnly => "This phone reads the household's keys; the phone that keeps the vault saves them.",
        _ => null,
    };

    public static WaitingNotice? Notice(VaultRestoreOutcome outcome, DateTimeOffset at) => outcome switch
    {
        VaultRestoreOutcome.WrongPassphrase => Problem(
            "The vault passphrase does not match", "The vault in Drive did not open with it. Check it against your other apps and enter it again.", at),
        VaultRestoreOutcome.Unreadable => Problem(
            "The vault in Drive could not be read", "The file is there but could not be opened. Your other apps' copies are untouched.", at),
        VaultRestoreOutcome.Failed => Problem(
            "The vault could not be reached", "Drive did not answer. Pulp keeps the keys it has and tries again next time it opens.", at),
        _ => null,
    };

    public static WaitingNotice? Notice(VaultSaveOutcome outcome, DateTimeOffset at) => outcome switch
    {
        VaultSaveOutcome.NotSignedIn => Problem("The keys were not saved", "Sign in with Google first. " + KeysLiveInTheVault, at),
        VaultSaveOutcome.NoPassphrase => Problem("The keys were not saved", "Enter the vault passphrase first. " + KeysLiveInTheVault, at),
        VaultSaveOutcome.Unreadable => Problem(
            "The keys were not saved", "The passphrase does not open the vault already in Drive, so nothing was written over it.", at),
        VaultSaveOutcome.WriteFailed => Problem(
            "The keys were not saved", "Drive would not take the vault. Sign in with Google again, then Save.", at),
        _ => null,
    };

    public static WaitingNotice SignInFailed(DateTimeOffset at) => new(
        SignInKey, "Google sign-in did not finish", "Without it Pulp cannot reach the vault in Drive. Try signing in again.",
        NoticeRole.Problem, WaitingNotices.OpenVaultLabel, NoticeAction.OpenVault, at);

    /// <summary>
    /// Google does not know this build yet. Retrying cannot help, so the notice names the one thing to
    /// register: the package and the suite's signing SHA-1, as an Android OAuth client.
    /// </summary>
    public static WaitingNotice SignInNotRegistered(DateTimeOffset at) => new(
        SignInKey, "Google does not recognise this build yet",
        $"Register the package {PulpIdentity.ApplicationId} with the suite's signing SHA-1 as an Android OAuth client "
            + "in the Google Cloud project, wait a few minutes, then sign in again.",
        NoticeRole.Problem, WaitingNotices.OpenVaultLabel, NoticeAction.OpenVault, at);

    private static WaitingNotice Problem(string headline, string detail, DateTimeOffset at) =>
        new(NoticeKey, headline, detail, NoticeRole.Problem, WaitingNotices.OpenVaultLabel, NoticeAction.OpenVault, at);
}
