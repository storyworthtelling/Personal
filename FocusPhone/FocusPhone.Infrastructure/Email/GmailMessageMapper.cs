using FocusPhone.Domain.Timeline;
using MimeKit;

namespace FocusPhone.Infrastructure.Email;

/// <summary>
/// Maps a Gmail MIME message onto the unified timeline: the contact key is the
/// other party (sender when incoming, first recipient when outgoing), and the
/// snippet is the subject or, failing that, the first line of the body.
/// </summary>
public class GmailMessageMapper
{
    private readonly string accountAddress;

    public GmailMessageMapper(string accountAddress)
    {
        this.accountAddress = ContactKeyNormalizer.NormalizeEmail(accountAddress);
    }

    public TimelineItem Map(MimeMessage message)
    {
        var from = message.From.Mailboxes.FirstOrDefault()?.Address ?? "";
        var incoming = ContactKeyNormalizer.NormalizeEmail(from) != accountAddress;
        var otherParty = incoming
            ? from
            : message.To.Mailboxes.FirstOrDefault()?.Address ?? "";

        return new TimelineItem(
            TimelineItemKind.Email,
            ContactKeyNormalizer.NormalizeEmail(otherParty),
            message.Date.ToUniversalTime(),
            Snippet(message),
            incoming,
            SourceId(message));
    }

    private static string Snippet(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            return message.Subject;
        }
        var firstLine = (message.TextBody ?? "").Split('\n').FirstOrDefault()?.Trim();
        return firstLine ?? "";
    }

    private static string SourceId(MimeMessage message) =>
        !string.IsNullOrEmpty(message.MessageId)
            ? message.MessageId
            // No Message-Id header: derive a stable id so re-syncs do not duplicate.
            : $"{message.Date.UtcTicks}:{message.Subject}";
}
