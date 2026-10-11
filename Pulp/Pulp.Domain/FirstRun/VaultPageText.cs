namespace Pulp.Domain.FirstRun;

/// <summary>The Vault page's words (D142, D272). Plain, and never voiced: this page holds keys, not occasions.</summary>
public static class VaultPageText
{
    public const string Title = "Vault";

    public const string AccountHeading = "Google account";

    public const string NotSignedIn = "Not signed in. Pulp finds the vault in this account's Drive.";

    public const string SignIn = "Sign in with Google";

    public const string SignInAgain = "Use another account";

    public const string PassphraseHeading = "Vault passphrase";

    public const string PassphraseHint = "The passphrase your other apps use";

    public const string PassphraseHelper =
        "The same passphrase as your other apps. It is shown so you can check it letter by letter; it is the one thing the vault cannot keep for you.";

    public const string KeysHeading = "Keys";

    public const string KeyHint = "Paste the key";

    public const string KeyHelper = "Not set. Paste the key, then Save; the vault keeps it from then on.";

    public const string Replace = "Replace";

    public const string Save = "Save";

    public const string Saving = "Saving to the vault…";

    public const string Opening = "Opening the vault…";

    public static string SignedInAs(string email) => $"Signed in as {email}.";
}
