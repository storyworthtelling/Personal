namespace Codex.Domain.Post;

/// <summary>
/// What was actually said in an MMS's WAP-push notification: enough to ask the carrier
/// for the message itself. This is not the MMS — it is the doorbell.
/// </summary>
public sealed record MmsNotification(string TransactionId, string ContentLocation);

/// <summary>
/// Parses the WAP push PDU Android hands the WAP_PUSH_DELIVER receiver's "data" extra —
/// an M-Notification.ind PDU (OMA-WAP-209-MMS-Encapsulation) — far enough to read the two
/// fields a download actually needs: the transaction id (echoed back on the download
/// request) and the content location (the URL to fetch the real message from).
///
/// This is deliberately not a general PDU parser. It walks the WSP header stream field by
/// field, and for any field it doesn't recognise it stops rather than guess: several WSP
/// value encodings (Encoded-string-value in particular) can't be skipped correctly without
/// knowing their exact shape, and mis-skipping one silently desyncs everything after it —
/// which would surface as a message that "arrives" with a garbage or truncated content
/// location, not as an error anywhere. The fields handled here cover every one that
/// realistically appears on a real M-Notification.ind (message type, transaction id, MMS
/// version, message class, message size, expiry, from, content location); field-name
/// values and shapes are taken from the OMA spec via the AOSP encoder/decoder
/// (frameworks/opt/mms's PduHeaders/PduParser), not reconstructed from memory.
/// </summary>
public static class MmsNotificationParser
{
    // WSP well-known header field-name octets (OMA-WAP-209-MMS-Encapsulation §7.3),
    // already carrying the high bit that marks a well-known (as opposed to literal-text)
    // field name.
    private const int FieldBcc = 0x81;
    private const int FieldCc = 0x82;
    private const int FieldContentLocation = 0x83;
    private const int FieldDeliveryTime = 0x87;
    private const int FieldExpiry = 0x88;
    private const int FieldFrom = 0x89;
    private const int FieldMessageClass = 0x8A;
    private const int FieldMessageId = 0x8B;
    private const int FieldMessageType = 0x8C;
    private const int FieldMmsVersion = 0x8D;
    private const int FieldMessageSize = 0x8E;
    private const int FieldPriority = 0x8F;
    private const int FieldReadReport = 0x90;
    private const int FieldReportAllowed = 0x91;
    private const int FieldResponseStatus = 0x92;
    private const int FieldResponseText = 0x93;
    private const int FieldSenderVisibility = 0x94;
    private const int FieldStatus = 0x95;
    private const int FieldSubject = 0x96;
    private const int FieldTo = 0x97;
    private const int FieldTransactionId = 0x98;
    private const int FieldReplyChargingDeadline = 0x9D;
    private const int FieldReplyChargingId = 0x9E;
    private const int FieldMessageCount = 0xAD;
    private const int FieldStart = 0xAF;
    private const int FieldReplaceId = 0xBD;
    private const int FieldCancelId = 0xBE;
    private const int FieldApplicId = 0xB7;
    private const int FieldReplyApplicId = 0xB8;
    private const int FieldAuxApplicId = 0xB9;
    private const int FieldLimit = 0xB3;

    private const int MessageTypeNotificationInd = 0x82;
    private const int MessageClassPersonalOrAbove = 0x80;

    private const int Quote = 0x7F;
    private const int EndOfString = 0x00;
    private const int ShortLengthMax = 30;
    private const int LengthQuote = 31;

    /// <summary>
    /// Text-String / Token-text fields: skipping one just means reading to the terminator,
    /// so they can share a code path with the two fields whose text we actually keep.
    /// </summary>
    private static readonly HashSet<int> TextStringFields =
    [
        FieldTransactionId, FieldMessageId, FieldReplaceId, FieldCancelId,
        FieldApplicId, FieldReplyApplicId, FieldAuxApplicId, FieldReplyChargingId,
    ];

    /// <summary>Single Octet value: always exactly one byte, safe to skip unconditionally.</summary>
    private static readonly HashSet<int> OctetFields =
    [
        FieldReadReport, FieldReportAllowed, FieldPriority, FieldStatus,
        FieldResponseStatus, FieldSenderVisibility, FieldMmsVersion,
    ];

    /// <summary>
    /// Value-length-prefixed compound values (Expiry/Delivery-Time/From and friends): the
    /// prefix alone is enough to skip past one without understanding what is inside it.
    /// </summary>
    private static readonly HashSet<int> ValueLengthFields =
    [
        FieldDeliveryTime, FieldExpiry, FieldReplyChargingDeadline, FieldFrom,
    ];

    public static MmsNotification? Parse(byte[]? pdu)
    {
        if (pdu is null || pdu.Length == 0)
        {
            return null;
        }

        var pos = 0;
        int? messageType = null;
        string? transactionId = null;
        string? contentLocation = null;

        try
        {
            while (pos < pdu.Length)
            {
                var field = pdu[pos++] & 0xFF;

                // A literal (non-well-known) header name: OMA-WAP-209 spells this out as
                // Token-text, but the value that follows could be any of several shapes we
                // don't distinguish here. Stop rather than misread the rest of the PDU as
                // garbage — the caller gets whatever was already recovered, or null.
                if (field is >= 0x20 and <= 0x7F)
                {
                    break;
                }

                if (field == FieldMessageType)
                {
                    messageType = ReadByte(pdu, ref pos);
                }
                else if (field == FieldTransactionId)
                {
                    transactionId = ReadTextString(pdu, ref pos);
                }
                else if (field == FieldContentLocation)
                {
                    contentLocation = ReadTextString(pdu, ref pos);
                }
                else if (TextStringFields.Contains(field))
                {
                    ReadTextString(pdu, ref pos);
                }
                else if (field == FieldMessageSize)
                {
                    SkipLongInteger(pdu, ref pos);
                }
                else if (field is FieldMessageCount or FieldStart or FieldLimit)
                {
                    SkipIntegerValue(pdu, ref pos);
                }
                else if (ValueLengthFields.Contains(field))
                {
                    SkipValueLengthBlock(pdu, ref pos);
                }
                else if (field == FieldMessageClass)
                {
                    // Message-class-value = Class-identifier (a single Octet) | Token-text.
                    // Only the well-known class identifiers are >= 0x80; anything else is a
                    // custom class name spelled out as text.
                    if ((pdu[pos] & 0xFF) >= MessageClassPersonalOrAbove)
                    {
                        pos++;
                    }
                    else
                    {
                        ReadTextString(pdu, ref pos);
                    }
                }
                else if (OctetFields.Contains(field))
                {
                    pos++;
                }
                else if (field is FieldBcc or FieldCc or FieldTo or FieldSubject or FieldResponseText)
                {
                    // Encoded-string-value: Text-string | (Value-length Char-set Text-string).
                    // Not needed for a notification-ind and not safe to skip generically
                    // (the two shapes overlap with plain text), so stop here too.
                    break;
                }
                else
                {
                    // An unhandled well-known field. Its value's shape isn't known here, so
                    // continuing would risk reading the rest of the PDU out of alignment.
                    break;
                }
            }
        }
        catch (IndexOutOfRangeException)
        {
            // Truncated or malformed PDU. Fall through and judge on whatever was recovered
            // before the stream ran out.
        }

        if (messageType != MessageTypeNotificationInd)
        {
            return null;
        }
        if (transactionId is null || contentLocation is null)
        {
            return null;
        }
        return new MmsNotification(transactionId, contentLocation);
    }

    private static int ReadByte(byte[] pdu, ref int pos) => pdu[pos++] & 0xFF;

    /// <summary>
    /// Text-string = [Quote] *TEXT End-of-string (wap-230-wsp §8.4.2.1). The leading Quote
    /// (0x7F) only appears when the first real character's high bit is set; otherwise the
    /// string starts immediately and ends at the NUL terminator.
    /// </summary>
    private static string ReadTextString(byte[] pdu, ref int pos)
    {
        if ((pdu[pos] & 0xFF) == Quote)
        {
            pos++;
        }
        var start = pos;
        while ((pdu[pos] & 0xFF) != EndOfString)
        {
            pos++;
        }
        // Text-string (as opposed to Encoded-string-value) carries no charset of its own;
        // WSP's own octets-128-255-need-a-Quote rule implies Latin-1, not UTF-8. In
        // practice a transaction id and a content-location URL are always plain ASCII,
        // which both encodings agree on.
        var text = System.Text.Encoding.Latin1.GetString(pdu, start, pos - start);
        pos++; // consume the terminator
        return text;
    }

    /// <summary>Value-length = Short-length (0-30) | (Length-quote(31) Uintvar-integer).</summary>
    private static int ReadValueLength(byte[] pdu, ref int pos)
    {
        var first = pdu[pos++] & 0xFF;
        if (first <= ShortLengthMax)
        {
            return first;
        }
        if (first == LengthQuote)
        {
            return (int)ReadUintvar(pdu, ref pos);
        }
        throw new IndexOutOfRangeException("Value-length > Length-quote");
    }

    /// <summary>Uintvar-integer: 7 bits per octet, continuation while the high bit is set.</summary>
    private static long ReadUintvar(byte[] pdu, ref int pos)
    {
        long result = 0;
        int b;
        do
        {
            b = pdu[pos++] & 0xFF;
            result = (result << 7) | (uint)(b & 0x7F);
        }
        while ((b & 0x80) != 0);
        return result;
    }

    /// <summary>Long-integer = Short-length Multi-octet-integer: skip the length-prefixed block.</summary>
    private static void SkipLongInteger(byte[] pdu, ref int pos)
    {
        var count = pdu[pos++] & 0xFF;
        pos += count;
    }

    /// <summary>Integer-Value = Short-integer (1 byte, high bit set) | Long-integer.</summary>
    private static void SkipIntegerValue(byte[] pdu, ref int pos)
    {
        if ((pdu[pos] & 0xFF) > 0x7F)
        {
            pos++;
        }
        else
        {
            SkipLongInteger(pdu, ref pos);
        }
    }

    /// <summary>
    /// A field whose whole value is prefixed by a Value-length: skip that many bytes
    /// without needing to understand what they encode.
    /// </summary>
    private static void SkipValueLengthBlock(byte[] pdu, ref int pos)
    {
        var length = ReadValueLength(pdu, ref pos);
        pos += length;
    }
}
