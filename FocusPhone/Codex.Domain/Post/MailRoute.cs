namespace Codex.Domain.Post;

/// <summary>How Post reaches the owner's mail.</summary>
public enum MailTransport
{
    /// <summary>Nothing configured; there is no mail.</summary>
    None,

    /// <summary>IMAP/SMTP on an app password. The old path, kept only until the API is proven on a device.</summary>
    AppPassword,

    /// <summary>The Gmail API on an OAuth token. A real trash, and search run by Gmail.</summary>
    GmailApi,
}

/// <summary>
/// Which mail path to use. This exists because the move to the Gmail API cannot be a flag
/// day: the API needs Post registered as an OAuth client with the mail scopes, and until
/// the owner has done that on their own Cloud project, the app password is the only thing
/// that reaches their mail at all. Shipping API-only would leave email dead in the window
/// between the two.
///
/// The API wins wherever it is available — it is the only path with a real, recoverable
/// trash and with search run by Gmail rather than over whatever is cached on the phone.
/// The app-password path goes away once the API is verified on a device.
/// </summary>
public static class MailRoute
{
    public static MailTransport Choose(bool googleSignedIn, bool appPasswordConfigured)
    {
        if (googleSignedIn)
        {
            return MailTransport.GmailApi;
        }
        return appPasswordConfigured ? MailTransport.AppPassword : MailTransport.None;
    }
}
