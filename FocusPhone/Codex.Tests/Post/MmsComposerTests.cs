using System.Text;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Verifies <see cref="MmsComposer"/> by decoding its own output — a from-scratch reader,
/// deliberately independent of the composer's internals, walking the same OMA-WAP-209 wire
/// format MmsNotificationParser reads on the receiving side. A composer that "looks right"
/// but writes one length wrong produces a message that some carriers accept and mangle
/// silently; decoding what was written is the only test that would catch that.
/// </summary>
public class MmsComposerTests
{
    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);

    [Fact]
    public void TextOnly_DecodesToTheExpectedHeadersAndOnePart()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-1", ["5551234567"], subject: null, text: "hi everyone", media: [], SentAt);

        var decoded = Decode(pdu);

        Assert.Equal(0x80, decoded.MessageType);
        Assert.Equal("txn-1", decoded.TransactionId);
        Assert.Equal(0x92, decoded.MmsVersion);
        Assert.Equal(SentAt.ToUnixTimeSeconds(), decoded.DateUnixSeconds);
        Assert.True(decoded.FromIsInsertToken);
        Assert.Equal(["5551234567"], decoded.ToAddresses);
        Assert.Null(decoded.Subject);
        Assert.Equal("application/vnd.wap.multipart.mixed", decoded.ContentType);
        var part = Assert.Single(decoded.Parts);
        Assert.Equal("text/plain", part.MimeType);
        Assert.Equal("hi everyone", Encoding.UTF8.GetString(part.Data));
    }

    [Fact]
    public void GroupRecipients_AllAppearAsSeparateToAddresses()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-2", ["5551110000", "5552220000", "5553330000"],
            subject: null, text: "group reply", media: [], SentAt);

        var decoded = Decode(pdu);

        Assert.Equal(["5551110000", "5552220000", "5553330000"], decoded.ToAddresses);
    }

    [Fact]
    public void TextAndMedia_BothArrivesAsSeparatePartsInOrder()
    {
        var photo = new MmsOutboundPart("photo.jpg", "image/jpeg", [1, 2, 3, 4, 5]);

        var pdu = MmsComposer.ComposeSendRequest(
            "txn-3", ["5551234567"], subject: null, text: "look at this", media: [photo], SentAt);

        var decoded = Decode(pdu);

        Assert.Equal(2, decoded.Parts.Count);
        Assert.Equal("text/plain", decoded.Parts[0].MimeType);
        Assert.Equal("look at this", Encoding.UTF8.GetString(decoded.Parts[0].Data));
        Assert.Equal("image/jpeg", decoded.Parts[1].MimeType);
        Assert.Equal("photo.jpg", decoded.Parts[1].FileName);
        Assert.Equal(photo.Data, decoded.Parts[1].Data);
    }

    [Fact]
    public void MediaOnly_NoTextPartIsAdded()
    {
        var video = new MmsOutboundPart("clip.mp4", "video/mp4", [9, 9, 9]);

        var pdu = MmsComposer.ComposeSendRequest(
            "txn-4", ["5551234567"], subject: null, text: null, media: [video], SentAt);

        var decoded = Decode(pdu);

        var part = Assert.Single(decoded.Parts);
        Assert.Equal("video/mp4", part.MimeType);
        Assert.Equal([9, 9, 9], part.Data);
    }

    [Fact]
    public void Subject_RoundTrips()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-5", ["5551234567"], subject: "Trip photos", text: "here they are", media: [], SentAt);

        Assert.Equal("Trip photos", Decode(pdu).Subject);
    }

    [Fact]
    public void MultipleAttachments_EachSurvivesWithItsOwnBytes()
    {
        var first = new MmsOutboundPart("a.jpg", "image/jpeg", [1, 1]);
        var second = new MmsOutboundPart("b.png", "image/png", [2, 2, 2]);

        var pdu = MmsComposer.ComposeSendRequest(
            "txn-6", ["5551234567"], subject: null, text: null, media: [first, second], SentAt);

        var decoded = Decode(pdu);

        Assert.Equal(2, decoded.Parts.Count);
        Assert.Equal([1, 1], decoded.Parts[0].Data);
        Assert.Equal([2, 2, 2], decoded.Parts[1].Data);
    }

    [Fact]
    public void NoRecipients_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MmsComposer.ComposeSendRequest("txn-7", [], null, "hi", [], SentAt));
    }

    [Fact]
    public void NoTextAndNoMedia_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MmsComposer.ComposeSendRequest("txn-8", ["5551234567"], null, null, [], SentAt));
    }

    [Fact]
    public void ATextByteWithTheHighBitSet_RoundTripsThroughTheQuoteRule()
    {
        // Exercises the Text-String Quote (0x7F) branch inside Encoded-string-value: a
        // subject whose first character has the high bit set needs the Quote marker so a
        // reader doesn't mistake it for the End-of-string it otherwise resembles.
        var subject = "Étude"; // "Étude", first byte's high bit set once Latin-1 encoded.

        var pdu = MmsComposer.ComposeSendRequest(
            "txn-9", ["5551234567"], subject, text: "x", media: [], SentAt);

        Assert.Equal(subject, Decode(pdu).Subject);
    }

    // --- A from-scratch M-Send.req reader, independent of MmsComposer's own code. ---

    private sealed record DecodedPdu(
        int MessageType, string TransactionId, int MmsVersion, long DateUnixSeconds,
        bool FromIsInsertToken, List<string> ToAddresses, string? Subject, string ContentType,
        List<DecodedPart> Parts);

    private sealed record DecodedPart(string MimeType, string? FileName, byte[] Data);

    private static DecodedPdu Decode(byte[] pdu)
    {
        var pos = 0;

        Expect(pdu, ref pos, 0x8C); // Message-Type field
        var messageType = pdu[pos++] & 0xFF;

        Expect(pdu, ref pos, 0x98); // Transaction-Id field
        var transactionId = ReadTextString(pdu, ref pos);

        Expect(pdu, ref pos, 0x8D); // MMS-Version field
        var mmsVersion = pdu[pos++] & 0xFF;

        Expect(pdu, ref pos, 0x85); // Date field
        var date = ReadLongInteger(pdu, ref pos);

        Expect(pdu, ref pos, 0x89); // From field
        var fromLength = ReadValueLength(pdu, ref pos);
        var fromIsInsertToken = fromLength == 1 && (pdu[pos] & 0xFF) == 0x81;
        pos += fromLength;

        var toAddresses = new List<string>();
        while ((pdu[pos] & 0xFF) == 0x97) // To field, repeated once per recipient
        {
            pos++;
            var address = ReadEncodedString(pdu, ref pos);
            toAddresses.Add(address.Split('/')[0]);
        }

        string? subject = null;
        if ((pdu[pos] & 0xFF) == 0x96) // Subject field, optional
        {
            pos++;
            subject = ReadEncodedString(pdu, ref pos);
        }

        Expect(pdu, ref pos, 0x84); // Content-Type field
        var contentTypeBlockLength = ReadValueLength(pdu, ref pos);
        var contentTypeStart = pos;
        var contentType = ReadTextString(pdu, ref pos);
        pos = contentTypeStart + contentTypeBlockLength; // skip any params after the value

        var partCount = (int)ReadUintvar(pdu, ref pos);
        var parts = new List<DecodedPart>();
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
                if (partField == 0x85) // PduPart's own "name" field
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

            parts.Add(new DecodedPart(mimeType, fileName, data));
        }

        return new DecodedPdu(
            messageType, transactionId, mmsVersion, date, fromIsInsertToken, toAddresses,
            subject, contentType, parts);
    }

    private static void Expect(byte[] pdu, ref int pos, int field)
    {
        var actual = pdu[pos++] & 0xFF;
        Assert.Equal(field, actual);
    }

    private static string ReadTextString(byte[] pdu, ref int pos)
    {
        if ((pdu[pos] & 0xFF) == 0x7F)
        {
            pos++;
        }
        var start = pos;
        while ((pdu[pos] & 0xFF) != 0x00)
        {
            pos++;
        }
        var text = Encoding.Latin1.GetString(pdu, start, pos - start);
        pos++;
        return text;
    }

    private static string ReadEncodedString(byte[] pdu, ref int pos)
    {
        var length = ReadValueLength(pdu, ref pos);
        var end = pos + length;
        pos++; // charset short-integer, not needed for these tests
        var text = ReadTextString(pdu, ref pos);
        pos = end;
        return text;
    }

    private static int ReadValueLength(byte[] pdu, ref int pos)
    {
        var first = pdu[pos++] & 0xFF;
        if (first <= 30)
        {
            return first;
        }
        return (int)ReadUintvar(pdu, ref pos);
    }

    private static long ReadLongInteger(byte[] pdu, ref int pos)
    {
        var count = pdu[pos++] & 0xFF;
        long value = 0;
        for (var i = 0; i < count; i++)
        {
            value = (value << 8) | (uint)(pdu[pos++] & 0xFF);
        }
        return value;
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
