using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Post is moving from IMAP/SMTP on an app password to the Gmail API on an OAuth token.
/// The move cannot be a flag day: the API needs Post registered as an OAuth client with
/// the mail scopes, and until the owner has done that, the app password is the only thing
/// that works. So both paths exist for one release and something has to choose.
/// </summary>
public class MailTransportTests
{
    // The API is preferred wherever it is available: it is the only path with a real
    // trash and server-side search, which is the whole reason for the move.
    [Fact]
    public void TheApiWinsWhenSignedIn()
    {
        Assert.Equal(MailTransport.GmailApi,
            MailRoute.Choose(googleSignedIn: true, appPasswordConfigured: true));
    }

    [Fact]
    public void TheAppPasswordCarriesOnUntilTheAccountIsSignedIn()
    {
        Assert.Equal(MailTransport.AppPassword,
            MailRoute.Choose(googleSignedIn: false, appPasswordConfigured: true));
    }

    [Fact]
    public void NeitherMeansNoMail()
    {
        Assert.Equal(MailTransport.None,
            MailRoute.Choose(googleSignedIn: false, appPasswordConfigured: false));
    }

    // Signing in is enough on its own: an owner who has moved to the API should not have
    // to keep an app password on file to be allowed to use it.
    [Fact]
    public void SigningInIsEnoughWithoutAnAppPassword()
    {
        Assert.Equal(MailTransport.GmailApi,
            MailRoute.Choose(googleSignedIn: true, appPasswordConfigured: false));
    }
}
