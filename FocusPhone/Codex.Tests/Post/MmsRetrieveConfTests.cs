using System.Text;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// The downloaded MMS itself (an M-Retrieve.conf PDU). As the default SMS app Post gets these
// bytes and nothing else: the platform does not store the message for the default app, so
// unless Post reads them, a group text or a picture never reaches the Mailbox.
//
// The PDUs are built here by hand, octet by octet, the way a carrier's MMSC writes them —
// independently of the parser — with field codes and value shapes from AOSP's PduHeaders,
// PduPart and PduContentTypes (frameworks/base/telephony/common/com/google/android/mms/pdu).
public class MmsRetrieveConfTests
{
    private const byte FieldContentType = 0x84;
    private const byte FieldDate = 0x85;
    private const byte FieldFrom = 0x89;
    private const byte FieldMessageClass = 0x8A;
    private const byte FieldMessageId = 0x8B;
    private const byte FieldMessageType = 0x8C;
    private const byte FieldMmsVersion = 0x8D;
    private const byte FieldPriority = 0x8F;
    private const byte FieldSubject = 0x96;
    private const byte FieldTo = 0x97;
    private const byte FieldTransactionId = 0x98;
    private const byte FieldRetrieveStatus = 0x99;
    private const byte FieldCc = 0x82;
    private const byte FieldDeliveryReport = 0x86;
    private const byte FieldReadReport = 0x90;

    private const byte RetrieveConf = 0x84;
    private const byte AddressPresentToken = 0x80;

    // Well-known content types, as Short-integer (index | 0x80).
    private const byte TextPlain = 0x80 | 0x03;
    private const byte ImageJpeg = 0x80 | 0x1E;
    private const byte MultipartRelated = 0x80 | 0x33;
    private const byte MultipartMixed = 0x80 | 0x23;

    // Content-type parameters and part headers.
    private const byte ParamCharset = 0x81;
    private const byte ParamType = 0x89;
    private const byte ParamStart = 0x8A;
    private const byte ParamName = 0x85;
    private const byte PartContentLocation = 0x8E;
    private const byte PartContentId = 0xC0;

    private const byte Utf8 = 0x80 | 0x6A;

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0xFF, 0xD9];

    private sealed class Pdu
    {
        private readonly List<byte> bytes = [];

        public Pdu Octet(params byte[] values)
        {
            bytes.AddRange(values);
            return this;
        }

        public Pdu Text(string value)
        {
            bytes.AddRange(Encoding.ASCII.GetBytes(value));
            bytes.Add(0);
            return this;
        }

        // Encoded-string-value with a charset: Value-length Charset Text-string.
        public Pdu Encoded(string value, byte charset = Utf8)
        {
            var text = Encoding.UTF8.GetBytes(value);
            var length = 1 + text.Length + 1;
            ValueLength(length);
            bytes.Add(charset);
            bytes.AddRange(text);
            bytes.Add(0);
            return this;
        }

        public Pdu ValueLength(int length)
        {
            if (length <= 30)
            {
                bytes.Add((byte)length);
            }
            else
            {
                bytes.Add(31);
                Uintvar(length);
            }
            return this;
        }

        public Pdu Uintvar(int value)
        {
            var groups = new List<byte>();
            do
            {
                groups.Insert(0, (byte)(value & 0x7F));
                value >>= 7;
            }
            while (value > 0);
            for (var i = 0; i < groups.Count - 1; i++)
            {
                groups[i] |= 0x80;
            }
            bytes.AddRange(groups);
            return this;
        }

        public Pdu Raw(byte[] data)
        {
            bytes.AddRange(data);
            return this;
        }

        public byte[] ToArray() => [.. bytes];

        public int Count => bytes.Count;
    }

    // One part: its headers (content type first) and its data.
    private static byte[] Part(byte[] headers, byte[] data) =>
        new Pdu().Uintvar(headers.Length).Uintvar(data.Length).Raw(headers).Raw(data).ToArray();

    private static byte[] SmilPartHeaders() =>
        new Pdu().Text("application/smil").Octet(PartContentId).Octet(0x22).Text("<smil>")
            .Octet(PartContentLocation).Text("smil.xml").ToArray();

    // Content-type with a charset parameter: Value-length Media Parameters.
    private static byte[] TextPartHeaders()
    {
        var parameters = new Pdu().Octet(TextPlain).Octet(ParamCharset, Utf8).Octet(ParamName).Text("text_0.txt").ToArray();
        return new Pdu().ValueLength(parameters.Length).Raw(parameters)
            .Octet(PartContentLocation).Text("text_0.txt").ToArray();
    }

    private static byte[] ImagePartHeaders()
    {
        var parameters = new Pdu().Octet(ImageJpeg).Octet(ParamName).Text("IMG_0001.jpg").ToArray();
        return new Pdu().ValueLength(parameters.Length).Raw(parameters)
            .Octet(PartContentLocation).Text("IMG_0001.jpg").ToArray();
    }

    private static byte[] VideoPartHeaders()
    {
        var parameters = new Pdu().Text("video/mp4").Octet(ParamName).Text("clip.mp4").ToArray();
        return new Pdu().ValueLength(parameters.Length).Raw(parameters).ToArray();
    }

    // A group picture message as a carrier delivers one: headers in carrier order, then a
    // multipart/related body of SMIL, text and a JPEG.
    private static byte[] GroupPicture(string? text = "Look at this")
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldTransactionId).Text("T17a2b")
            .Octet(FieldMmsVersion, 0x92)
            .Octet(FieldMessageId).Text("MSG-0001@mmsc.example")
            .Octet(FieldDate, 0x04, 0x68, 0x4E, 0xE1, 0x80)
            .Octet(FieldFrom).ValueLength(1 + 1 + "+15550100001/TYPE=PLMN".Length + 1 + 1)
            .Octet(AddressPresentToken).Encoded("+15550100001/TYPE=PLMN")
            .Octet(FieldTo).Encoded("+15550100002/TYPE=PLMN")
            .Octet(FieldTo).Text("+15550100009/TYPE=PLMN")
            .Octet(FieldCc).Encoded("+15550100003/TYPE=PLMN")
            .Octet(FieldSubject).Encoded("Weekend")
            .Octet(FieldMessageClass, 0x80)
            .Octet(FieldPriority, 0x81)
            .Octet(FieldDeliveryReport, 0x81)
            .Octet(FieldReadReport, 0x81)
            .Octet(FieldRetrieveStatus, 0x80);

        var typeParameters = new Pdu().Octet(MultipartRelated)
            .Octet(ParamStart).Text("<smil>")
            .Octet(ParamType).Text("application/smil").ToArray();
        pdu.Octet(FieldContentType).ValueLength(typeParameters.Length).Raw(typeParameters);

        var parts = new List<byte[]> { Part(SmilPartHeaders(), Encoding.ASCII.GetBytes("<smil><body/></smil>")) };
        if (text is not null)
        {
            parts.Add(Part(TextPartHeaders(), Encoding.UTF8.GetBytes(text)));
        }
        parts.Add(Part(ImagePartHeaders(), Jpeg));

        pdu.Uintvar(parts.Count);
        foreach (var part in parts)
        {
            pdu.Raw(part);
        }
        return pdu.ToArray();
    }

    [Fact]
    public void ReadsTheSenderWithoutTheTypeSuffix()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        Assert.Equal("+15550100001", message.From);
    }

    [Fact]
    public void ReadsEveryRecipientWhicheverWayItIsEncoded()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        Assert.Equal(["+15550100002", "+15550100009"], message.To);
        Assert.Equal(["+15550100003"], message.Cc);
    }

    [Fact]
    public void ReadsTheHeadersAPersistedMessageNeeds()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        Assert.Equal("T17a2b", message.TransactionId);
        Assert.Equal("MSG-0001@mmsc.example", message.MessageId);
        Assert.Equal("Weekend", message.Subject);
        Assert.Equal(0x684EE180, message.DateSeconds);
        Assert.Equal("application/vnd.wap.multipart.related", message.ContentType);
        Assert.Equal(0x12, message.MmsVersion);
    }

    [Fact]
    public void ReadsEveryPartWithItsTypeAndBytes()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        Assert.Equal(["application/smil", "text/plain", "image/jpeg"], message.Parts.Select(part => part.ContentType));
        Assert.Equal(Jpeg, message.Parts[2].Data);
        Assert.Equal("IMG_0001.jpg", message.Parts[2].Name);
        Assert.Equal("IMG_0001.jpg", message.Parts[2].ContentLocation);
        Assert.Equal("<smil>", message.Parts[0].ContentId);
    }

    [Fact]
    public void DecodesTextInItsDeclaredCharset()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture("Café at 7? 🙂"))!;

        var text = message.Parts.Single(part => part.ContentType == "text/plain");
        Assert.Equal(0x6A, text.Charset);
        Assert.Equal("Café at 7? 🙂", text.Text());
    }

    [Fact]
    public void ReadsAVideoPartTypedAsText()
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldMmsVersion, 0x92)
            .Octet(FieldFrom).ValueLength(1 + 1 + 12 + 1 + 1).Octet(AddressPresentToken).Encoded("+15550100001")
            .Octet(FieldContentType, MultipartMixed)
            .Uintvar(1)
            .Raw(Part(VideoPartHeaders(), [1, 2, 3]))
            .ToArray();

        var part = Assert.Single(MmsRetrieveConfParser.Parse(pdu)!.Parts);

        Assert.Equal("video/mp4", part.ContentType);
        Assert.Equal("clip.mp4", part.Name);
        Assert.Equal([1, 2, 3], part.Data);
    }

    [Fact]
    public void SkipsACustomTextHeader()
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Text("X-Carrier-Note").Text("anything")
            .Octet(FieldMmsVersion, 0x92)
            .Octet(FieldFrom).ValueLength(1 + 1 + 12 + 1 + 1).Octet(AddressPresentToken).Encoded("+15550100001")
            .Octet(FieldContentType, MultipartMixed)
            .Uintvar(0)
            .ToArray();

        Assert.Equal("+15550100001", MmsRetrieveConfParser.Parse(pdu)!.From);
    }

    // A long Value-length (quote + uintvar) and a part over 127 bytes, so the multi-octet
    // uintvar path is exercised the way any real photo exercises it.
    [Fact]
    public void ReadsALargePart()
    {
        var picture = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldMmsVersion, 0x92)
            .Octet(FieldSubject).Encoded(new string('s', 40))
            .Octet(FieldContentType, MultipartMixed)
            .Uintvar(1)
            .Raw(Part(ImagePartHeaders(), picture))
            .ToArray();

        var message = MmsRetrieveConfParser.Parse(pdu)!;

        Assert.Equal(new string('s', 40), message.Subject);
        Assert.Equal(picture, Assert.Single(message.Parts).Data);
    }

    [Fact]
    public void ANotificationIsNotARetrievedMessage()
    {
        var pdu = new Pdu().Octet(FieldMessageType, 0x82).Octet(FieldTransactionId).Text("T1").ToArray();

        Assert.Null(MmsRetrieveConfParser.Parse(pdu));
    }

    [Fact]
    public void ATruncatedMessageIsRefusedNotHalfRead()
    {
        var whole = GroupPicture();

        Assert.Null(MmsRetrieveConfParser.Parse(whole[..(whole.Length - 5)]));
        Assert.Null(MmsRetrieveConfParser.Parse([]));
        Assert.Null(MmsRetrieveConfParser.Parse(null));
    }

    // The carrier's own refusal comes back as a retrieve-conf with an error status and no
    // body worth storing.
    [Fact]
    public void ACarrierErrorIsReportedAsNotOk()
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldMmsVersion, 0x92)
            .Octet(FieldRetrieveStatus, 0xC0)
            .ToArray();

        var message = MmsRetrieveConfParser.Parse(pdu);

        Assert.NotNull(message);
        Assert.False(message!.IsRetrieved);
    }

    [Fact]
    public void InsertAddressToken_LeavesNoSender()
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldFrom, 0x01, 0x81)
            .Octet(FieldContentType, MultipartMixed)
            .Uintvar(0)
            .ToArray();

        Assert.Null(MmsRetrieveConfParser.Parse(pdu)!.From);
    }

    // Thread membership: everyone on the message but this phone, so the conversation lands
    // where Android's own thread for that group is — the same one a reply goes to.
    [Fact]
    public void ThreadRecipients_AreEveryoneButThisPhone()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        var recipients = message.ThreadRecipients(["(555) 010-0009"]);

        Assert.Equal(["+15550100001", "+15550100002", "+15550100003"], recipients);
    }

    [Fact]
    public void ThreadRecipients_WithThisPhoneUnknown_KeepEveryone()
    {
        var message = MmsRetrieveConfParser.Parse(GroupPicture())!;

        Assert.Equal(4, message.ThreadRecipients([]).Count);
    }

    // A one-to-one picture is addressed only to this phone: the thread is the sender's.
    [Fact]
    public void ThreadRecipients_OneToOne_IsTheSender()
    {
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldFrom).ValueLength(1 + 1 + 12 + 1 + 1).Octet(AddressPresentToken).Encoded("+15550100001")
            .Octet(FieldTo).Text("+15550100009/TYPE=PLMN")
            .Octet(FieldContentType, MultipartMixed)
            .Uintvar(0)
            .ToArray();

        Assert.Equal(["+15550100001"], MmsRetrieveConfParser.Parse(pdu)!.ThreadRecipients([]));
    }

    [Fact]
    public void NotifyResponse_IsTheAcknowledgementAospWrites()
    {
        var pdu = MmsComposer.ComposeNotifyResponse("T17a2b");

        Assert.Equal(
            new Pdu().Octet(FieldMessageType, 0x83).Octet(FieldTransactionId).Text("T17a2b")
                .Octet(FieldMmsVersion, 0x92).Octet(0x95, 0x81).ToArray(),
            pdu);
    }

    // UTF-16 is a two-octet MIBenum (1015) and its text is full of zero octets, so the
    // string has to be bounded by its Value-length, not by the first zero.
    [Fact]
    public void ReadsAUtf16Subject()
    {
        var text = Encoding.BigEndianUnicode.GetBytes("Hi");
        var pdu = new Pdu()
            .Octet(FieldMessageType, RetrieveConf)
            .Octet(FieldSubject).ValueLength(3 + text.Length + 1).Octet(0x02, 0x03, 0xF7).Raw(text).Octet(0)
            .Octet(FieldMmsVersion, 0x92)
            .ToArray();

        var message = MmsRetrieveConfParser.Parse(pdu)!;

        Assert.Equal("Hi", message.Subject);
        Assert.Equal(0x12, message.MmsVersion);
    }
}
