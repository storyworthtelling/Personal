namespace Codex.Domain.Credentials;

/// <summary>
/// Stable identifiers for the app-specific credentials the FocusPhone heads track. The
/// vault passphrase is deliberately absent — it is never stored in the vault and is always
/// shown in the clear, so it has no "hide while healthy" behaviour to drive.
///
/// These live in Domain rather than beside the settings screens because both ends need
/// them: a service client reports health at its HTTP choke point, and a settings screen
/// reads it back. Anywhere else and one of those two would be holding a copy of the
/// string.
/// </summary>
public static class CredentialIds
{
    /// <summary>Post: the Gmail app password, used by the IMAP/SMTP path.</summary>
    public const string GmailAppPassword = "gmail-app-password";

    /// <summary>Post: the Google account behind the Gmail API, which replaces it.</summary>
    public const string GmailAccount = "gmail-account";

    /// <summary>Flyleaf: the GitHub token for the post-lockdown update channel.</summary>
    public const string GitHubUpdateToken = "github-update-token";
}
