namespace Codex.Domain.Post;

/// <summary>Enough of a composed M-Send.req to write the local "sent" record for it.</summary>
public sealed record MmsSendRequestInfo(
    IReadOnlyList<string> Recipients, string? Subject, IReadOnlyList<MmsOutboundPart> Parts);

/// <summary>
/// Reads back the recipients, subject and parts from a PDU <see cref="MmsComposer"/> just
/// wrote. Purely a persistence aid: once <c>SmsManager.SendMultimediaMessage</c> reports
/// success, the app still has to write its own "sent" record into the provider — Android
/// does not do that for us, the same as it does not for a sent SMS — and rather than pass
/// the original recipients/text/media across a process boundary that a killed-and-restarted
/// app cannot be trusted to still hold, this re-reads them from the PDU file itself, which
/// is what survived. It only understands the shape <see cref="MmsComposer"/> writes (fixed
/// header order, no SMIL, no Cc/Bcc, Extension-Media content types throughout); it is not a
/// general M-Send.req reader.
/// </summary>
public static class MmsSendRequestParser
{
    private const int FieldTo = 0x97;
    private const int FieldSubject = 0x96;
    private const int FieldContentType = 0x84;
    private const int PartName = 0x85;
    private const int Quote = 0x7F;
    private const int EndOfString = 0x00;
    private const int ShortLengthMax = 30;

    public static MmsSendRequestInfo? Parse(byte[]? pdu)
    {
        if (pdu is null || pdu.Length == 0)
        {
            return null;
        }

        try
        {
            var pos = 0;
            // Message-Type, Transaction-Id, MMS-Version, Date, From: each a fixed shape
            // this composer always writes, skipped without needing their values here.
            pos++; pos++; // Message-Type field + value
            pos++; SkipTextString(pdu, ref pos); // Transaction-Id
            pos++; pos++; // MMS-Version field + value
            pos++; SkipLongInteger(pdu, ref pos); // Date
            pos++; var fromLength = ReadValueLength(pdu, ref pos); pos += fromLength; // From

            var recipients = new List<string>();
            while ((pdu[pos] & 0xFF) == FieldTo)
            {
                pos++;
                var address = ReadEncodedString(pdu, ref pos);
                recipients.Add(address.Split('/')[0]);
            }

            string? subject = null;
            if ((pdu[pos] & 0xFF) == FieldSubject)
            {
                pos++;
                subject = ReadEncodedString(pdu, ref pos);
            }

            if ((pdu[pos++] & 0xFF) != FieldContentType)
            {
                return null;
            }
            var contentTypeBlockLength = ReadValueLength(pdu, ref pos);
            pos += contentTypeBlockLength; // the top-level content-type value itself; unused here

            var partCount = (int)ReadUintvar(pdu, ref pos);
            var parts = new List<MmsOutboundPart>();
            for (var i = 0; i < partCount; i++)
            {
                var headerLength = (int)ReadUintvar(pdu, ref pos);
                var dataLength = (int)ReadUintvar(pdu, ref pos);
                var headerStart = pos;

                var ctLength = ReadValueLength(pdu, ref pos);
                var ctStart = pos;
                var mimeType = ReadTextString(pdu, ref pos);
                string? fileName = null;
                while (pos < ctStart + ctLength)
                {
                    var partField = pdu[pos++] & 0xFF;
                    if (partField == PartName)
                    {
                        fileName = ReadTextString(pdu, ref pos);
                    }
                    else
                    {
                        break;
                    }
                }
                pos = headerStart + headerLength;

                var data = pdu[pos..(pos + dataLength)];
                pos += dataLength;

                parts.Add(new MmsOutboundPart(fileName ?? "attachment", mimeType, data));
            }

            return recipients.Count == 0 ? null : new MmsSendRequestInfo(recipients, subject, parts);
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static void SkipTextString(byte[] pdu, ref int pos) => ReadTextString(pdu, ref pos);

    private static void SkipLongInteger(byte[] pdu, ref int pos)
    {
        var count = pdu[pos++] & 0xFF;
        pos += count;
    }

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
        var text = System.Text.Encoding.Latin1.GetString(pdu, start, pos - start);
        pos++;
        return text;
    }

    private static string ReadEncodedString(byte[] pdu, ref int pos)
    {
        var length = ReadValueLength(pdu, ref pos);
        var end = pos + length;
        pos++; // charset short-integer
        var text = ReadTextString(pdu, ref pos);
        pos = end;
        return text;
    }

    private static int ReadValueLength(byte[] pdu, ref int pos)
    {
        var first = pdu[pos++] & 0xFF;
        return first <= ShortLengthMax ? first : (int)ReadUintvar(pdu, ref pos);
    }

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
}
