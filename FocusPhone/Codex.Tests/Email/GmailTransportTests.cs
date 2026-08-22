using Codex.Infrastructure.Email;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using Xunit;

namespace Codex.Tests.Email;

public class GmailTransportTests
{
    // Android cannot complete OCSP/CRL revocation checks for Gmail's chain, so
    // MailKit's default revocation check fails the handshake with
    // Chain_RevocationStatusUnknown. ConfigureTransport turns it off; these guard
    // against the config call being dropped or the default flipping back.

    [Fact]
    public void ConfigureTransport_DisablesRevocationCheck_ForImap()
    {
        using var imap = new ImapClient();
        GmailClient.ConfigureTransport(imap);
        Assert.False(imap.CheckCertificateRevocation);
    }

    [Fact]
    public void ConfigureTransport_DisablesRevocationCheck_ForSmtp()
    {
        using var smtp = new SmtpClient();
        GmailClient.ConfigureTransport(smtp);
        Assert.False(smtp.CheckCertificateRevocation);
    }
}
