namespace Codex.Ui.Voice;

// Stable identifiers for the app-specific credentials the FocusPhone heads track. The vault passphrase is
// deliberately absent — it's never stored in the vault and is always shown in the clear, so it has no
// "hide while healthy" behaviour to drive.
public static class CredentialIds
{
    // Communication head: the Gmail app password used for IMAP/SMTP.
    public const string GmailAppPassword = "gmail-app-password";
    // Launcher head: the GitHub token for the post-lockdown update channel.
    public const string GitHubUpdateToken = "github-update-token";
}
