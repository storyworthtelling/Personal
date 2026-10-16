using System.Text;
using Codex.Domain.Timeline;

namespace Codex.Domain.Post;

/// <summary>One part of a downloaded MMS: a picture, a video, its text, or its SMIL layout.</summary>
public sealed record RetrievedPart(
    string ContentType,
    int Charset,
    string? Name,
    string? ContentLocation,
    string? ContentId,
    byte[] Data)
{
    /// <summary>The part's bytes as text, in the charset it declared (UTF-8 when it declared none).</summary>
    public string Text() => MmsCharsets.Decode(Data, Charset);
}

/// <summary>
/// A downloaded MMS — an M-Retrieve.conf PDU — read far enough to store it: who sent it, who
/// else it went to, when, and every part. The platform hands the default SMS app these bytes
/// and stores nothing itself, so this is the whole of what a group text or a picture is
/// before Post writes it into the provider.
/// </summary>
public sealed record RetrievedMms(
    string? TransactionId,
    string? MessageId,
    string? From,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    string? Subject,
    long? DateSeconds,
    int? MmsVersion,
    int? RetrieveStatus,
    string? ContentType,
    IReadOnlyList<RetrievedPart> Parts)
{
    private const int RetrieveStatusOk = 0x80;

    /// <summary>False when the carrier answered with an error rather than the message.</summary>
    public bool IsRetrieved => RetrieveStatus is null or RetrieveStatusOk;

    /// <summary>
    /// The addresses Android's conversation for this message is made of: the sender and
    /// everyone else it went to, but not this phone — the set a reply goes to, so the reply
    /// lands in the same thread. Follows AOSP's PduPersister: a "to" list of one is taken to
    /// be this phone alone, and any of <paramref name="ownNumbers"/> is left out. With this
    /// phone's number unknown, everyone stays, which keeps the thread consistent with a reply
    /// built from the same addresses.
    /// </summary>
    public IReadOnlyList<string> ThreadRecipients(IReadOnlyCollection<string> ownNumbers)
    {
        var own = ownNumbers.Select(ContactKeyNormalizer.NormalizePhone).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var recipients = new List<string>();

        void Add(string address, bool excludeOwn)
        {
            var key = ContactKeyNormalizer.NormalizePhone(address);
            if ((excludeOwn && own.Contains(key)) || !seen.Add(key))
            {
                return;
            }
            recipients.Add(address);
        }

        if (From is not null)
        {
            Add(From, excludeOwn: false);
        }
        if (To.Count > 1)
        {
            foreach (var address in To)
            {
                Add(address, excludeOwn: true);
            }
        }
        foreach (var address in Cc)
        {
            Add(address, excludeOwn: true);
        }
        return recipients;
    }
}

/// <summary>
/// Parses an M-Retrieve.conf PDU (OMA-WAP-209-MMS-Encapsulation). A port of the parts of
/// AOSP's <c>PduParser</c> that a received message passes through — <c>parseHeaders</c>,
/// <c>parseParts</c>, <c>parseContentType</c>, <c>parsePartHeaders</c> and their helpers
/// (frameworks/base/telephony/common/com/google/android/mms/pdu) — with every header field
/// read in the shape AOSP reads it, so an unexpected field is consumed rather than read out
/// of alignment. Two places are stricter than AOSP: an Encoded-string-value is bounded by
/// its own Value-length (AOSP reads a multi-octet charset as one octet and drifts), and a
/// PDU that ends early is refused rather than half-stored.
/// </summary>
public static class MmsRetrieveConfParser
{
    private const int Bcc = 0x81;
    private const int Cc = 0x82;
    private const int ContentLocation = 0x83;
    private const int ContentType = 0x84;
    private const int Date = 0x85;
    private const int DeliveryReport = 0x86;
    private const int DeliveryTime = 0x87;
    private const int Expiry = 0x88;
    private const int From = 0x89;
    private const int MessageClass = 0x8A;
    private const int MessageId = 0x8B;
    private const int MessageType = 0x8C;
    private const int MmsVersion = 0x8D;
    private const int MessageSize = 0x8E;
    private const int Priority = 0x8F;
    private const int ReadReport = 0x90;
    private const int ReportAllowed = 0x91;
    private const int ResponseStatus = 0x92;
    private const int ResponseText = 0x93;
    private const int SenderVisibility = 0x94;
    private const int Status = 0x95;
    private const int Subject = 0x96;
    private const int To = 0x97;
    private const int TransactionId = 0x98;
    private const int RetrieveStatus = 0x99;
    private const int RetrieveText = 0x9A;
    private const int ReadStatus = 0x9B;
    private const int ReplyCharging = 0x9C;
    private const int ReplyChargingDeadline = 0x9D;
    private const int ReplyChargingId = 0x9E;
    private const int ReplyChargingSize = 0x9F;
    private const int PreviouslySentBy = 0xA0;
    private const int PreviouslySentDate = 0xA1;
    private const int Store = 0xA2;
    private const int MmState = 0xA3;
    private const int MmFlags = 0xA4;
    private const int StoreStatus = 0xA5;
    private const int StoreStatusText = 0xA6;
    private const int Stored = 0xA7;
    private const int Totals = 0xA9;
    private const int MboxTotals = 0xAA;
    private const int Quotas = 0xAB;
    private const int MboxQuotas = 0xAC;
    private const int MessageCount = 0xAD;
    private const int Start = 0xAF;
    private const int DistributionIndicator = 0xB1;
    private const int ElementDescriptor = 0xB2;
    private const int Limit = 0xB3;
    private const int RecommendedRetrievalMode = 0xB4;
    private const int RecommendedRetrievalModeText = 0xB5;
    private const int StatusText = 0xB6;
    private const int ApplicId = 0xB7;
    private const int ReplyApplicId = 0xB8;
    private const int AuxApplicId = 0xB9;
    private const int ContentClass = 0xBA;
    private const int DrmContent = 0xBB;
    private const int AdaptationAllowed = 0xBC;
    private const int ReplaceId = 0xBD;
    private const int CancelId = 0xBE;
    private const int CancelStatus = 0xBF;

    private const int MessageTypeRetrieveConf = 0x84;
    private const int AddressPresentToken = 0x80;

    // Content-type parameters (PduPart).
    private const int ParamCharset = 0x81;
    private const int ParamType = 0x83;
    private const int ParamDepName = 0x85;
    private const int ParamCtMrType = 0x89;
    private const int ParamDepStart = 0x8A;
    private const int ParamName = 0x97;
    private const int ParamStart = 0x99;

    // Part headers (PduPart).
    private const int PartContentLocation = 0x8E;
    private const int PartContentId = 0xC0;
    private const int PartDepContentDisposition = 0xAE;
    private const int PartContentDisposition = 0xC5;

    private const int TextMin = 32;
    private const int TextMax = 127;
    private const int Quote = 127;
    private const int QuotedStringFlag = 34;
    private const int ShortLengthMax = 30;
    private const int LengthQuote = 31;
    private const int LongIntegerLengthMax = 8;

    private const string MultipartAlternative = "application/vnd.wap.multipart.alternative";

    public static RetrievedMms? Parse(byte[]? pdu)
    {
        if (pdu is null || pdu.Length == 0)
        {
            return null;
        }
        try
        {
            return new Reader(pdu).ReadMessage();
        }
        catch (MalformedPduException)
        {
            return null;
        }
    }

    private sealed class MalformedPduException(string message) : Exception(message);

    private sealed class Reader(byte[] pdu)
    {
        private int pos;

        public RetrievedMms? ReadMessage()
        {
            int? messageType = null;
            string? transactionId = null;
            string? messageId = null;
            string? from = null;
            var to = new List<string>();
            var cc = new List<string>();
            string? subject = null;
            long? date = null;
            int? version = null;
            int? retrieveStatus = null;
            string? contentType = null;

            while (pos < pdu.Length && contentType is null)
            {
                var field = Peek();
                if (field is >= TextMin and <= TextMax)
                {
                    // A custom header: its name, then its value, both as text.
                    ReadTextString();
                    ReadTextString();
                    continue;
                }
                pos++;
                switch (field)
                {
                    case MessageType:
                        messageType = Byte();
                        break;
                    case RetrieveStatus:
                        retrieveStatus = Byte();
                        break;
                    case ReportAllowed or AdaptationAllowed or DeliveryReport or DrmContent
                        or DistributionIndicator or Quotas or ReadReport or Store or Stored or Totals
                        or SenderVisibility or ReadStatus or CancelStatus or Priority or Status
                        or ReplyCharging or MmState or RecommendedRetrievalMode or ContentClass
                        or StoreStatus or ResponseStatus:
                        Byte();
                        break;
                    case Date:
                        date = ReadLongInteger();
                        break;
                    case ReplyChargingSize or MessageSize:
                        ReadLongInteger();
                        break;
                    case MessageCount or Start or Limit:
                        ReadIntegerValue();
                        break;
                    case TransactionId:
                        transactionId = ReadTextString();
                        break;
                    case MessageId:
                        messageId = ReadTextString();
                        break;
                    case ReplyChargingId or AuxApplicId or ApplicId or ReplyApplicId or ReplaceId
                        or CancelId or ContentLocation:
                        ReadTextString();
                        break;
                    case Subject:
                        subject = ReadEncodedString();
                        break;
                    case RecommendedRetrievalModeText or RetrieveText or StatusText or StoreStatusText
                        or ResponseText:
                        ReadEncodedString();
                        break;
                    case To:
                        AddAddress(to, ReadEncodedString());
                        break;
                    case Cc:
                        AddAddress(cc, ReadEncodedString());
                        break;
                    case Bcc:
                        ReadEncodedString();
                        break;
                    case DeliveryTime or Expiry or ReplyChargingDeadline or PreviouslySentBy
                        or PreviouslySentDate or MmFlags or MboxTotals or MboxQuotas:
                        SkipValueLengthBlock();
                        break;
                    case From:
                        from = ReadFrom();
                        break;
                    case MessageClass:
                        if (Peek() >= 0x80)
                        {
                            pos++;
                        }
                        else
                        {
                            ReadTextString();
                        }
                        break;
                    case MmsVersion:
                        // Short-integer: the provider's "v" column holds the bare value (0x12 for 1.2).
                        version = Byte() & 0x7F;
                        break;
                    case ElementDescriptor:
                        ReadContentType(null);
                        break;
                    case ContentType:
                        contentType = ReadContentType(null);
                        break;
                    default:
                        // AOSP logs an unknown well-known header and carries on with the next
                        // octet; so does this, to read what AOSP reads.
                        break;
                }
            }

            if (messageType != MessageTypeRetrieveConf)
            {
                return null;
            }

            var parts = new List<RetrievedPart>();
            if (contentType is not null && pos < pdu.Length)
            {
                if (contentType.StartsWith("application/vnd.wap.multipart.", StringComparison.OrdinalIgnoreCase))
                {
                    parts.AddRange(ReadParts());
                }
                else
                {
                    // A single-part message: the rest of the PDU is the content itself.
                    parts.Add(new RetrievedPart(contentType, 0, null, null, null, pdu[pos..]));
                }
            }

            return new RetrievedMms(
                transactionId, messageId, from, to, cc, subject, date, version, retrieveStatus, contentType, parts);
        }

        private static void AddAddress(List<string> addresses, string? value)
        {
            var address = WithoutTypeSuffix(value);
            if (!string.IsNullOrEmpty(address))
            {
                addresses.Add(address);
            }
        }

        // "+15550100001/TYPE=PLMN" → "+15550100001", as PduParser does.
        private static string? WithoutTypeSuffix(string? value)
        {
            if (value is null)
            {
                return null;
            }
            var slash = value.IndexOf('/');
            return slash > 0 ? value[..slash] : value;
        }

        // From-value = Value-length (Address-present-token Encoded-string-value | Insert-address-token)
        private string? ReadFrom()
        {
            var length = ReadValueLength();
            var end = pos + length;
            string? from = null;
            if (Byte() == AddressPresentToken)
            {
                from = WithoutTypeSuffix(ReadEncodedString());
            }
            pos = Bounded(end);
            return string.IsNullOrEmpty(from) ? null : from;
        }

        private List<RetrievedPart> ReadParts()
        {
            var count = ReadUintvar();
            var parts = new List<RetrievedPart>(Math.Min(count, 64));
            for (var i = 0; i < count; i++)
            {
                var headerLength = ReadUintvar();
                var dataLength = ReadUintvar();
                var headersEnd = Bounded(pos + headerLength);

                var parameters = new Dictionary<int, object>();
                var contentType = ReadContentType(parameters) ?? "*/*";
                string? contentLocation = null;
                string? contentId = null;
                while (pos < headersEnd)
                {
                    var header = Byte();
                    if (header > TextMax)
                    {
                        switch (header)
                        {
                            case PartContentLocation:
                                contentLocation = ReadTextString();
                                break;
                            case PartContentId:
                                contentId = ReadQuotedString();
                                break;
                            case PartContentDisposition or PartDepContentDisposition:
                                SkipValueLengthBlock();
                                break;
                            default:
                                // An unsupported part header: AOSP skips the rest of the headers.
                                pos = headersEnd;
                                break;
                        }
                    }
                    else if (header >= TextMin)
                    {
                        pos--;
                        ReadTextString();
                        ReadTextString();
                    }
                    else
                    {
                        pos = headersEnd;
                    }
                }
                pos = headersEnd;

                var data = Take(dataLength);
                if (string.Equals(contentType, MultipartAlternative, StringComparison.OrdinalIgnoreCase))
                {
                    // The alternatives are one thing said several ways; AOSP keeps the first.
                    var first = new Reader(data).ReadParts().FirstOrDefault();
                    if (first is not null)
                    {
                        parts.Add(first);
                    }
                    continue;
                }

                parts.Add(new RetrievedPart(
                    contentType,
                    parameters.TryGetValue(ParamCharset, out var charset) ? (int)charset : 0,
                    parameters.TryGetValue(ParamName, out var name) ? (string)name : null,
                    contentLocation,
                    contentId,
                    data));
            }
            return parts;
        }

        // Content-type-value = Constrained-media | Content-general-form
        private string? ReadContentType(Dictionary<int, object>? parameters)
        {
            var first = Peek();
            if (first < TextMin)
            {
                var length = ReadValueLength();
                var end = Bounded(pos + length);
                var media = Peek();
                string? type;
                if (media is >= TextMin and <= TextMax)
                {
                    type = ReadTextString();
                }
                else if (media > TextMax)
                {
                    type = WellKnownType(Byte() & 0x7F);
                }
                else
                {
                    pos = end;
                    return "*/*";
                }
                ReadParameters(parameters, end);
                pos = end;
                return type;
            }
            if (first <= TextMax)
            {
                return ReadTextString();
            }
            return WellKnownType(Byte() & 0x7F);
        }

        private void ReadParameters(Dictionary<int, object>? parameters, int end)
        {
            while (pos < end)
            {
                var parameter = Byte();
                switch (parameter)
                {
                    case ParamType or ParamCtMrType:
                        var type = Peek() > TextMax ? WellKnownType(Byte() & 0x7F) : ReadTextString();
                        if (type is not null)
                        {
                            parameters?.TryAdd(ParamType, type);
                        }
                        break;
                    case ParamStart or ParamDepStart:
                        ReadTextString();
                        break;
                    case ParamCharset:
                        var value = Peek();
                        if ((value > TextMin && value < TextMax) || value == 0)
                        {
                            parameters?.TryAdd(ParamCharset, MmsCharsets.FromName(ReadTextString()));
                        }
                        else
                        {
                            parameters?.TryAdd(ParamCharset, (int)ReadIntegerValue());
                        }
                        break;
                    case ParamName or ParamDepName:
                        var name = ReadTextString();
                        if (name is not null)
                        {
                            parameters?.TryAdd(ParamName, name);
                        }
                        break;
                    default:
                        // An unsupported parameter: AOSP skips the rest of them.
                        pos = end;
                        break;
                }
            }
        }

        private static string? WellKnownType(int index) =>
            index < MmsContentTypes.WellKnown.Count ? MmsContentTypes.WellKnown[index] : null;

        // Encoded-string-value = Text-string | Value-length Char-set Text-string
        private string? ReadEncodedString()
        {
            var first = Peek();
            if (first == 0)
            {
                pos++;
                return string.Empty;
            }
            if (first < TextMin)
            {
                var length = ReadValueLength();
                var end = Bounded(pos + length);
                var charset = (int)ReadIntegerValue();
                // The text runs to the Value-length's end, not to the first zero octet: a
                // UTF-16 string is full of them.
                var start = pos < end && pdu[pos] == Quote ? pos + 1 : pos;
                var stop = end > start && pdu[end - 1] == 0 ? end - 1 : end;
                pos = end;
                return MmsCharsets.Decode(pdu[start..Math.Max(start, stop)], charset);
            }
            return MmsCharsets.Decode(ReadTextBytes(), MmsCharsets.Utf8);
        }

        private string? ReadTextString()
        {
            var bytes = ReadTextBytes();
            return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
        }

        // Text-string = [Quote] *TEXT End-of-string
        private byte[] ReadTextBytes()
        {
            if (Peek() == Quote)
            {
                pos++;
            }
            return ReadToTerminator();
        }

        // Quoted-string = <Octet 34> *TEXT End-of-string, as Content-ID is written.
        private string? ReadQuotedString()
        {
            if (Peek() == QuotedStringFlag)
            {
                pos++;
            }
            var bytes = ReadToTerminator();
            return bytes.Length == 0 ? null : Encoding.UTF8.GetString(bytes);
        }

        private byte[] ReadToTerminator()
        {
            var start = pos;
            while (Peek() != 0)
            {
                pos++;
            }
            var bytes = pdu[start..pos];
            pos++;
            return bytes;
        }

        private int ReadValueLength()
        {
            var first = Byte();
            if (first <= ShortLengthMax)
            {
                return first;
            }
            if (first == LengthQuote)
            {
                return ReadUintvar();
            }
            throw new MalformedPduException($"Value-length octet {first} is above Length-quote");
        }

        private int ReadUintvar()
        {
            long result = 0;
            int octet;
            var octets = 0;
            do
            {
                octet = Byte();
                result = (result << 7) | (uint)(octet & 0x7F);
                if (++octets > 5 || result > int.MaxValue)
                {
                    throw new MalformedPduException("Uintvar longer than a message can be");
                }
            }
            while ((octet & 0x80) != 0);
            return (int)result;
        }

        private long ReadLongInteger()
        {
            var count = Byte();
            if (count > LongIntegerLengthMax)
            {
                throw new MalformedPduException($"Long-integer of {count} octets");
            }
            long result = 0;
            for (var i = 0; i < count; i++)
            {
                result = (result << 8) | (uint)Byte();
            }
            return result;
        }

        // Integer-Value = Short-integer | Long-integer
        private long ReadIntegerValue() => Peek() > 0x7F ? Byte() & 0x7F : ReadLongInteger();

        private void SkipValueLengthBlock()
        {
            var length = ReadValueLength();
            pos = Bounded(pos + length);
        }

        private byte[] Take(int count)
        {
            var end = Bounded(pos + count);
            var bytes = pdu[pos..end];
            pos = end;
            return bytes;
        }

        private int Bounded(int end) =>
            end <= pdu.Length && end >= pos
                ? end
                : throw new MalformedPduException("A length runs past the end of the message");

        private int Peek() =>
            pos < pdu.Length ? pdu[pos] : throw new MalformedPduException("The message ends early");

        private int Byte()
        {
            var value = Peek();
            pos++;
            return value;
        }
    }
}

/// <summary>WSP's well-known content types, by Short-integer index (AOSP PduContentTypes).</summary>
public static class MmsContentTypes
{
    public static readonly IReadOnlyList<string> WellKnown =
    [
        "*/*", "text/*", "text/html", "text/plain", "text/x-hdml", "text/x-ttml", "text/x-vCalendar",
        "text/x-vCard", "text/vnd.wap.wml", "text/vnd.wap.wmlscript", "text/vnd.wap.wta-event",
        "multipart/*", "multipart/mixed", "multipart/form-data", "multipart/byterantes",
        "multipart/alternative", "application/*", "application/java-vm",
        "application/x-www-form-urlencoded", "application/x-hdmlc", "application/vnd.wap.wmlc",
        "application/vnd.wap.wmlscriptc", "application/vnd.wap.wta-eventc", "application/vnd.wap.uaprof",
        "application/vnd.wap.wtls-ca-certificate", "application/vnd.wap.wtls-user-certificate",
        "application/x-x509-ca-cert", "application/x-x509-user-cert", "image/*", "image/gif", "image/jpeg",
        "image/tiff", "image/png", "image/vnd.wap.wbmp", "application/vnd.wap.multipart.*",
        "application/vnd.wap.multipart.mixed", "application/vnd.wap.multipart.form-data",
        "application/vnd.wap.multipart.byteranges", "application/vnd.wap.multipart.alternative",
        "application/xml", "text/xml", "application/vnd.wap.wbxml", "application/x-x968-cross-cert",
        "application/x-x968-ca-cert", "application/x-x968-user-cert", "text/vnd.wap.si",
        "application/vnd.wap.sic", "text/vnd.wap.sl", "application/vnd.wap.slc", "text/vnd.wap.co",
        "application/vnd.wap.coc", "application/vnd.wap.multipart.related", "application/vnd.wap.sia",
        "text/vnd.wap.connectivity-xml", "application/vnd.wap.connectivity-wbxml",
        "application/pkcs7-mime", "application/vnd.wap.hashed-certificate",
        "application/vnd.wap.signed-certificate", "application/vnd.wap.cert-response",
        "application/xhtml+xml", "application/wml+xml", "text/css", "application/vnd.wap.mms-message",
        "application/vnd.wap.rollover-certificate", "application/vnd.wap.locc+wbxml",
        "application/vnd.wap.loc+xml", "application/vnd.syncml.dm+wbxml", "application/vnd.syncml.dm+xml",
        "application/vnd.syncml.notification", "application/vnd.wap.xhtml+xml", "application/vnd.wv.csp.cir",
        "application/vnd.oma.dd+xml", "application/vnd.oma.drm.message", "application/vnd.oma.drm.content",
        "application/vnd.oma.drm.rights+xml", "application/vnd.oma.drm.rights+wbxml",
        "application/vnd.wv.csp+xml", "application/vnd.wv.csp+wbxml", "application/vnd.syncml.ds.notification",
        "audio/*", "video/*", "application/vnd.oma.dd2+xml", "application/mikey",
    ];
}

/// <summary>The IANA MIBenum charsets an MMS names (AOSP CharacterSets), and decoding by them.</summary>
public static class MmsCharsets
{
    public const int Utf8 = 0x6A;
    private const int UsAscii = 0x03;
    private const int Latin1 = 0x04;
    private const int Ucs2 = 0x03E8;
    private const int Utf16 = 0x03F7;

    public static int FromName(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "utf-8" => Utf8,
        "us-ascii" => UsAscii,
        "iso-8859-1" => Latin1,
        "iso-10646-ucs-2" => Ucs2,
        "utf-16" => Utf16,
        _ => 0,
    };

    // Anything unnamed or unknown is read as UTF-8, as AOSP's EncodedStringValue does.
    public static string Decode(byte[] bytes, int charset) => charset switch
    {
        UsAscii => Encoding.ASCII.GetString(bytes),
        Latin1 => Encoding.Latin1.GetString(bytes),
        Ucs2 => Encoding.BigEndianUnicode.GetString(bytes),
        Utf16 => DecodeUtf16(bytes),
        _ => Encoding.UTF8.GetString(bytes),
    };

    private static string DecodeUtf16(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        }
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        }
        return Encoding.BigEndianUnicode.GetString(bytes);
    }
}
