using System.Linq;
using System.Text;

namespace Codex.Domain.Post;

/// <summary>One thing to attach to an outgoing MMS: a filename, its MIME type, and its bytes.</summary>
public sealed record MmsOutboundPart(string FileName, string MimeType, byte[] Data);

/// <summary>Thrown instead of composing a PDU that would hold the whole file in memory twice over.</summary>
public sealed class MmsAttachmentTooLargeException(long actualBytes, long maxBytes)
    : Exception($"Attachment is {actualBytes:N0} bytes, over the {maxBytes:N0}-byte limit.")
{
    public long ActualBytes { get; } = actualBytes;
    public long MaxBytes { get; } = maxBytes;
}

/// <summary>
/// Builds an M-Send.req PDU (OMA-WAP-209-MMS-Encapsulation) — the wire format
/// <see cref="Android.Telephony.SmsManager.SendMultimediaMessage"/> expects to find at the
/// content Uri it is given. This is the send-side counterpart of
/// <see cref="MmsNotificationParser"/>: field-name octets, value shapes and the multipart
/// body layout are taken from AOSP's own encoder (frameworks/opt/mms's PduComposer/
/// PduContentTypes), not reconstructed from memory, for the same reason — a wrong byte here
/// does not throw, it just produces a message that silently confuses or is rejected by
/// whatever reads it downstream.
///
/// Two simplifications versus what a real Android Messages app writes, both deliberate:
///
/// The body is always <c>application/vnd.wap.multipart.mixed</c>, never
/// <c>multipart/related</c> with a SMIL presentation. SMIL exists to time a slideshow (this
/// picture, then that one, each with a caption on a timeline); Post attaches, it does not
/// stage a slideshow, so the simpler container is both sufficient and correct — the MMS spec
/// does not require SMIL, only conventional phone software adds it for a richer viewing
/// experience.
///
/// Every content-type value (the message's own, and each part's) is written as literal text
/// (WSP's Extension-Media) rather than looked up in the ~130-entry well-known short-integer
/// table the spec also allows. Extension-Media is valid everywhere Constrained-media is
/// legal — AOSP's own composer already falls back to it for any part whose type is not in
/// that table — so using it uniformly avoids porting the table at the cost of a few extra
/// bytes per message, which is irrelevant next to a photo's size.
/// </summary>
public static class MmsComposer
{
    private const int MessageTypeSendReq = 0x80;
    private const int CurrentMmsVersion = 0x12; // MMS 1.2: (1 << 4) | 2, per OMA-WAP-209.
    private const int FromInsertAddressToken = 0x81;
    private const int Utf8CharacterSet = 0x6A; // IANA MIBenum 106.
    private const string ContentTypeMultipartMixed = "application/vnd.wap.multipart.mixed";

    // A guess, not a measured carrier limit: most US MMSCs cap well under this. It exists so
    // an oversized attachment fails predictably here rather than as an OutOfMemoryException
    // partway through building the wire buffer.
    public const long MaxMediaBytes = 15 * 1024 * 1024;

    // WSP well-known header field-name octets (high bit already set), matching the ones
    // MmsNotificationParser reads on the receiving side.
    private const int FieldMessageType = 0x8C;
    private const int FieldTransactionId = 0x98;
    private const int FieldMmsVersion = 0x8D;
    private const int FieldDate = 0x85;
    private const int FieldFrom = 0x89;
    private const int FieldTo = 0x97;
    private const int FieldSubject = 0x96;
    private const int FieldContentType = 0x84;

    // PduPart header field-name octets (a separate namespace from the message-level ones
    // above — WSP reuses small integers per context).
    private const int PartName = 0x85;

    private const int MessageTypeNotifyRespInd = 0x83;
    private const int FieldStatus = 0x95;
    private const int StatusRetrieved = 0x81;

    /// <summary>
    /// The M-NotifyResp.ind that tells the carrier a downloaded MMS arrived, as AOSP's
    /// PduComposer writes it: message type, the notification's transaction id, MMS version,
    /// and Status = Retrieved. Without it some carriers announce the same message again, and
    /// the owner gets it twice.
    /// </summary>
    public static byte[] ComposeNotifyResponse(string transactionId)
    {
        var writer = new PduWriter();
        writer.AppendOctet(FieldMessageType);
        writer.AppendOctet(MessageTypeNotifyRespInd);
        writer.AppendOctet(FieldTransactionId);
        writer.AppendTextString(transactionId);
        writer.AppendOctet(FieldMmsVersion);
        writer.AppendShortInteger(CurrentMmsVersion);
        writer.AppendOctet(FieldStatus);
        writer.AppendOctet(StatusRetrieved);
        return writer.ToArray();
    }

    /// <summary>
    /// <paramref name="recipients"/> must be non-empty; each is written as a phone-number
    /// address (<c>/TYPE=PLMN</c>, per WSP's address-type suffix convention) since that is
    /// the only address shape Post's own contacts are keyed by. The From field is always
    /// the insert-address-token — "let the carrier fill in the sender" — because Post has no
    /// reliable way to read the device's own number back out of the platform.
    /// </summary>
    public static byte[] ComposeSendRequest(
        string transactionId,
        IReadOnlyList<string> recipients,
        string? subject,
        string? text,
        IReadOnlyList<MmsOutboundPart> media,
        DateTimeOffset sentAtUtc)
    {
        if (recipients.Count == 0)
        {
            throw new ArgumentException("An MMS needs at least one recipient.", nameof(recipients));
        }
        if (string.IsNullOrEmpty(text) && media.Count == 0)
        {
            throw new ArgumentException("An MMS needs a message or an attachment.", nameof(text));
        }
        var mediaBytes = media.Sum(part => (long)part.Data.Length);
        if (mediaBytes > MaxMediaBytes)
        {
            // A cap rather than none at all: without one, a picked video is read fully into
            // memory here and again in the wire buffer below, which is how a phone-sized
            // video turns into an OutOfMemoryException instead of a message the owner can
            // act on. The number itself is a conservative guess, not a measured carrier
            // limit — most US MMSCs cap well under this, so this exists to fail predictably
            // before that point, not to promise anything will actually be accepted.
            throw new MmsAttachmentTooLargeException(mediaBytes, MaxMediaBytes);
        }

        var writer = new PduWriter();

        writer.AppendOctet(FieldMessageType);
        writer.AppendOctet(MessageTypeSendReq);

        writer.AppendOctet(FieldTransactionId);
        writer.AppendTextString(transactionId);

        writer.AppendOctet(FieldMmsVersion);
        writer.AppendShortInteger(CurrentMmsVersion);

        writer.AppendOctet(FieldDate);
        writer.AppendLongInteger(sentAtUtc.ToUnixTimeSeconds());

        writer.AppendOctet(FieldFrom);
        writer.AppendValueLength(1);
        writer.AppendOctet(FromInsertAddressToken);

        foreach (var recipient in recipients)
        {
            writer.AppendOctet(FieldTo);
            writer.AppendEncodedString($"{recipient}/TYPE=PLMN");
        }

        if (!string.IsNullOrEmpty(subject))
        {
            writer.AppendOctet(FieldSubject);
            writer.AppendEncodedString(subject);
        }

        writer.AppendOctet(FieldContentType);
        writer.AppendValueLengthWrapped(inner => inner.AppendTextString(ContentTypeMultipartMixed));

        var parts = new List<MmsOutboundPart>();
        if (!string.IsNullOrEmpty(text))
        {
            parts.Add(new MmsOutboundPart("text.txt", "text/plain", Encoding.UTF8.GetBytes(text)));
        }
        parts.AddRange(media);

        writer.AppendUintvar((ulong)parts.Count);
        foreach (var part in parts)
        {
            AppendPart(writer, part);
        }

        return writer.ToArray();
    }

    private static void AppendPart(PduWriter writer, MmsOutboundPart part)
    {
        var headers = new PduWriter();
        headers.AppendValueLengthWrapped(inner =>
        {
            inner.AppendTextString(part.MimeType);
            inner.AppendOctet(PartName);
            inner.AppendTextString(part.FileName);
        });
        var headerBytes = headers.ToArray();

        writer.AppendUintvar((ulong)headerBytes.Length);
        writer.AppendUintvar((ulong)part.Data.Length);
        writer.AppendBytes(headerBytes);
        writer.AppendBytes(part.Data);
    }

    /// <summary>
    /// The handful of WSP (wap-230-wsp) primitive encodings an M-Send.req needs, in one
    /// place so the field-composing code above reads as a list of fields rather than a
    /// tangle of bit-twiddling. Appends to a growable buffer; <see cref="ToArray"/> reads it
    /// back for the fields (Content-Type, a part's own headers) that need their own byte
    /// count measured before being wrapped in a Value-Length prefix.
    /// </summary>
    private sealed class PduWriter
    {
        private readonly List<byte> buffer = [];

        public void AppendOctet(int value) => buffer.Add((byte)value);

        public void AppendBytes(byte[] bytes) => buffer.AddRange(bytes);

        /// <summary>Short-integer = one octet, high bit set, value in the low 7 bits.</summary>
        public void AppendShortInteger(int value) => buffer.Add((byte)(value | 0x80));

        /// <summary>Long-integer = Short-length Multi-octet-integer (big-endian, minimal length).</summary>
        public void AppendLongInteger(long value)
        {
            var bytes = new List<byte>();
            var remaining = value;
            do
            {
                bytes.Insert(0, (byte)(remaining & 0xFF));
                remaining >>= 8;
            }
            while (remaining != 0);
            buffer.Add((byte)bytes.Count);
            buffer.AddRange(bytes);
        }

        /// <summary>Uintvar-integer: 7 bits per octet, continuation bit set on every octet but the last.</summary>
        public void AppendUintvar(ulong value)
        {
            var groups = new List<byte> { (byte)(value & 0x7F) };
            value >>= 7;
            while (value > 0)
            {
                groups.Add((byte)((value & 0x7F) | 0x80));
                value >>= 7;
            }
            groups.Reverse();
            buffer.AddRange(groups);
        }

        /// <summary>Value-length = Short-length (0-30) | (Length-quote(31) Uintvar-integer).</summary>
        public void AppendValueLength(int length)
        {
            const int shortLengthMax = 30;
            const int lengthQuote = 31;
            if (length <= shortLengthMax)
            {
                buffer.Add((byte)length);
            }
            else
            {
                buffer.Add(lengthQuote);
                AppendUintvar((ulong)length);
            }
        }

        /// <summary>Text-string = [Quote(0x7F) if the first byte's high bit is set] *TEXT End-of-string(0x00).</summary>
        public void AppendTextString(string text)
        {
            var bytes = Encoding.Latin1.GetBytes(text);
            if (bytes.Length > 0 && (bytes[0] & 0x80) != 0)
            {
                buffer.Add(0x7F);
            }
            buffer.AddRange(bytes);
            buffer.Add(0x00);
        }

        /// <summary>
        /// Encoded-string-value = Value-length Char-set Text-string. Unlike a bare
        /// Text-string this always carries an explicit charset (UTF-8), which is what lets
        /// an address's "/TYPE=PLMN" suffix or a subject line round-trip unambiguously.
        /// </summary>
        public void AppendEncodedString(string text) => AppendValueLengthWrapped(inner =>
        {
            inner.AppendShortInteger(Utf8CharacterSet);
            inner.AppendTextString(text);
        });

        /// <summary>
        /// Writes <paramref name="write"/>'s output into a nested buffer, measures it, then
        /// appends [Value-Length][that buffer] here — the pattern every length-prefixed
        /// compound value in this format uses (Encoded-string-value, Content-Type, a part's
        /// own header block).
        /// </summary>
        public void AppendValueLengthWrapped(Action<PduWriter> write)
        {
            var inner = new PduWriter();
            write(inner);
            var bytes = inner.ToArray();
            AppendValueLength(bytes.Length);
            buffer.AddRange(bytes);
        }

        public byte[] ToArray() => [.. buffer];
    }
}
