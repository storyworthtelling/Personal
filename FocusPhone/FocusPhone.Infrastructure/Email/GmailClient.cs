using FocusPhone.Domain.Timeline;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using MimeKit;

namespace FocusPhone.Infrastructure.Email;

/// <summary>
/// Gmail over IMAP/SMTP with an app password (2-Step Verification required).
/// Transport-only: all mapping logic lives in the unit-tested
/// GmailMessageMapper. Instances are single-use per sync to keep connection
/// lifetimes simple on a phone.
/// </summary>
public class GmailClient
{
    private const string ImapHost = "imap.gmail.com";
    private const string SmtpHost = "smtp.gmail.com";

    private readonly string accountAddress;
    private readonly string appPassword;
    private readonly GmailMessageMapper mapper;

    public GmailClient(string accountAddress, string appPassword)
    {
        // A pasted app password arrives with Google's display spaces and the
        // address may carry stray whitespace/case; normalise before either
        // reaches the server, otherwise auth fails with
        // "Application-specific password required".
        this.accountAddress = GmailCredentials.NormalizeAddress(accountAddress);
        this.appPassword = GmailCredentials.NormalizeAppPassword(appPassword);
        mapper = new GmailMessageMapper(this.accountAddress);
    }

    /// <summary>The normalised Gmail address the client authenticates with.</summary>
    public string AccountAddress => accountAddress;

    /// <summary>The normalised app password the client authenticates with.</summary>
    public string AppPassword => appPassword;

    /// <summary>
    /// Fetches recent inbox and sent messages (both directions of the
    /// conversation) newer than the given cutoff.
    /// </summary>
    public async Task<IReadOnlyList<TimelineItem>> FetchRecentAsync(
        DateTimeOffset since, CancellationToken cancellation = default)
    {
        using var imap = new ImapClient();
        ConfigureTransport(imap);
        await imap.ConnectAsync(ImapHost, 993, useSsl: true, cancellation);
        await imap.AuthenticateAsync(accountAddress, appPassword, cancellation);

        var items = new List<TimelineItem>();
        foreach (var folderName in new[] { "INBOX", "[Gmail]/Sent Mail" })
        {
            var folder = await imap.GetFolderAsync(folderName, cancellation);
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellation);
            var ids = await folder.SearchAsync(
                SearchQuery.DeliveredAfter(since.UtcDateTime), cancellation);
            foreach (var id in ids)
            {
                items.Add(mapper.Map(await folder.GetMessageAsync(id, cancellation)));
            }
        }

        await imap.DisconnectAsync(quit: true, cancellation);
        return items;
    }

    /// <summary>
    /// Sends an email. Quick reply passes just to/subject/body; full compose adds
    /// cc, file attachments, and the In-Reply-To header so the reply threads with
    /// the message it answers.
    /// </summary>
    public async Task SendAsync(string to, string subject, string body,
        IReadOnlyList<string>? cc = null, IReadOnlyList<string>? attachmentPaths = null,
        string? inReplyTo = null, CancellationToken cancellation = default)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(accountAddress));
        message.To.Add(MailboxAddress.Parse(to));
        foreach (var address in cc ?? [])
        {
            message.Cc.Add(MailboxAddress.Parse(address));
        }
        message.Subject = subject;
        if (!string.IsNullOrEmpty(inReplyTo))
        {
            message.InReplyTo = inReplyTo;
            message.References.Add(inReplyTo);
        }

        var builder = new BodyBuilder { TextBody = body };
        foreach (var path in attachmentPaths ?? [])
        {
            await builder.Attachments.AddAsync(path, cancellation);
        }
        message.Body = builder.ToMessageBody();

        using var smtp = new SmtpClient();
        ConfigureTransport(smtp);
        await smtp.ConnectAsync(SmtpHost, 465, useSsl: true, cancellation);
        await smtp.AuthenticateAsync(accountAddress, appPassword, cancellation);
        await smtp.SendAsync(message, cancellation);
        await smtp.DisconnectAsync(quit: true, cancellation);
    }

    /// <summary>
    /// Deletes an email by moving every copy carrying the given RFC Message-Id
    /// from the inbox to Gmail's Trash — the closest thing IMAP offers to a real
    /// delete. Called only after the undo window elapses.
    /// </summary>
    public async Task DeleteAsync(string messageId, CancellationToken cancellation = default)
    {
        using var imap = new ImapClient();
        ConfigureTransport(imap);
        await imap.ConnectAsync(ImapHost, 993, useSsl: true, cancellation);
        await imap.AuthenticateAsync(accountAddress, appPassword, cancellation);

        var inbox = imap.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, cancellation);
        var matches = await inbox.SearchAsync(
            SearchQuery.HeaderContains("Message-Id", messageId), cancellation);
        if (matches.Count > 0)
        {
            var trash = await imap.GetFolderAsync("[Gmail]/Trash", cancellation);
            await inbox.MoveToAsync(matches, trash, cancellation);
        }

        await imap.DisconnectAsync(quit: true, cancellation);
    }

    /// <summary>
    /// Applies the TLS settings needed to reach Gmail from Android. The device
    /// cannot complete OCSP/CRL revocation checks for Gmail's chain (the
    /// intermediate certificate publishes no OCSP responder), so MailKit's default
    /// revocation check fails the handshake with Chain_RevocationStatusUnknown.
    /// Gmail's chain is otherwise valid, so revocation checking is turned off.
    /// </summary>
    internal static void ConfigureTransport(IMailService client)
    {
        client.CheckCertificateRevocation = false;
    }
}
