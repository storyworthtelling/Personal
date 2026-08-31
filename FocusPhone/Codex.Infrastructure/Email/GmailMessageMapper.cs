using Codex.Domain.Post;
using Codex.Domain.Timeline;
using MimeKit;

namespace Codex.Infrastructure.Email;

/// <summary>
/// Maps a Gmail MIME message onto the unified timeline: the contact key is the
/// other party (sender when incoming, first recipient when outgoing), and the
/// snippet is the subject or, failing that, the first line of the body. The
/// email-specific fields (subject/body/recipients/threading headers,
/// attachments) are carried alongside so the conversation view can quote-reply
/// and render attachments without re-fetching.
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
            SourceId(message))
        {
            Subject = message.Subject,
            Body = ReadableBody(message),
            FromAddress = from,
            Recipients = message.To.Mailboxes.Select(box => box.Address).ToList(),
            CarbonCopy = message.Cc.Mailboxes.Select(box => box.Address).ToList(),
            MessageId = string.IsNullOrEmpty(message.MessageId) ? null : message.MessageId,
            InReplyTo = string.IsNullOrEmpty(message.InReplyTo) ? null : message.InReplyTo,
            Attachments = MapAttachments(message),
        };
    }

    /// <summary>
    /// What the owner should see. An HTML-only message has NO plain-text part, so reading
    /// TextBody alone leaves the body null — the message lists, opens, and shows nothing,
    /// with no error anywhere because nothing failed. Until Post has a real renderer (the
    /// hardened web view is still Flyleaf's alone), the HTML is reduced to readable text.
    /// </summary>
    private static string? ReadableBody(MimeMessage message) =>
        message.TextBody ?? (message.HtmlBody is { } html ? HtmlToText.Convert(html) : null);

    /// <summary>Whether a message carried each kind of body, for the field notes.</summary>
    public static (bool HasTextPart, bool HasHtmlPart) BodyShape(MimeMessage message) =>
        (message.TextBody is not null, message.HtmlBody is not null);

    /// <summary>
    /// Attachment metadata, with real sizes. The whole RFC822 message is already in memory
    /// on the API path, so the size is there to be read — it used to be hardcoded to zero,
    /// which meant nothing could tell a signature image from a ten-megabyte PDF.
    /// </summary>
    private static IReadOnlyList<AttachmentRef> MapAttachments(MimeMessage message) =>
        message.Attachments
            .OfType<MimePart>()
            .Select((part, index) => new AttachmentRef(
                Id: string.IsNullOrEmpty(part.ContentId) ? $"att{index}" : part.ContentId,
                FileName: part.FileName,
                MimeType: part.ContentType.MimeType,
                SizeBytes: MeasureContent(part)))
            .ToList();

    // The decoded length, which is what a size is worth knowing for. Measured rather than
    // materialized: a count is wanted, not the bytes.
    private static long MeasureContent(MimePart part)
    {
        try
        {
            if (part.Content is null)
            {
                return 0;
            }
            using var counter = new MeasuringStream();
            part.Content.DecodeTo(counter);
            return counter.Length;
        }
        catch (Exception)
        {
            // A malformed part must not cost the whole message; an unknown size is fine.
            return 0;
        }
    }

    /// <summary>Counts bytes written and keeps none of them.</summary>
    private sealed class MeasuringStream : Stream
    {
        private long written;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => written;
        public override long Position { get => written; set => throw new NotSupportedException(); }

        public override void Write(byte[] buffer, int offset, int count) => written += count;
        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private static string Snippet(MimeMessage message)
    {
        if (!string.IsNullOrWhiteSpace(message.Subject))
        {
            return message.Subject;
        }
        var firstLine = (ReadableBody(message) ?? "").Split('\n').FirstOrDefault()?.Trim();
        return firstLine ?? "";
    }

    private static string SourceId(MimeMessage message) =>
        !string.IsNullOrEmpty(message.MessageId)
            ? message.MessageId
            // No Message-Id header: derive a stable id so re-syncs do not duplicate.
            : $"{message.Date.UtcTicks}:{message.Subject}";
}
