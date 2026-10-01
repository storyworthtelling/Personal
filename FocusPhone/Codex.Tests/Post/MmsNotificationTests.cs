using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

public class MmsNotificationTests
{
    private const int FieldMessageType = 0x8C;
    private const int FieldTransactionId = 0x98;
    private const int FieldMmsVersion = 0x8D;
    private const int FieldFrom = 0x89;
    private const int FieldMessageClass = 0x8A;
    private const int FieldMessageSize = 0x8E;
    private const int FieldExpiry = 0x88;
    private const int FieldContentLocation = 0x83;

    private const int MessageTypeNotificationInd = 0x82;
    private const int MessageTypeRetrieveConf = 0x84;

    // Builds a realistic M-Notification.ind PDU: Message-Type, Transaction-Id, MMS-Version,
    // From (insert-address-token), Message-Class, Message-Size, Expiry, Content-Location —
    // the field set and order a real carrier notification uses.
    private static byte[] NotificationIndPdu(
        string transactionId = "1234ABCD",
        string contentLocation = "http://mmsc.carrier.example/path?id=12345",
        int messageType = MessageTypeNotificationInd)
    {
        var bytes = new List<byte>();

        bytes.Add(FieldMessageType);
        bytes.Add((byte)messageType);

        bytes.Add(FieldTransactionId);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(transactionId));
        bytes.Add(0x00);

        bytes.Add(FieldMmsVersion);
        bytes.Add(0x90); // MMS-Version, arbitrary short-integer with the high bit set

        // From: Value-length(1) + insert-address-token(one arbitrary byte).
        bytes.Add(FieldFrom);
        bytes.Add(0x01);
        bytes.Add(0x81);

        // Message-Class: a well-known class identifier (>= 0x80), a single Octet.
        bytes.Add(FieldMessageClass);
        bytes.Add(0x80);

        // Message-Size: Long-Integer, length 2, value 1024 (0x0400).
        bytes.Add(FieldMessageSize);
        bytes.Add(0x02);
        bytes.Add(0x04);
        bytes.Add(0x00);

        // Expiry: Value-length(6) + Absolute-token(0x80) + Long-Integer(length 4, value 1).
        bytes.Add(FieldExpiry);
        bytes.Add(0x06);
        bytes.Add(0x80);
        bytes.Add(0x04);
        bytes.AddRange([0x00, 0x00, 0x00, 0x01]);

        bytes.Add(FieldContentLocation);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(contentLocation));
        bytes.Add(0x00);

        return [.. bytes];
    }

    [Fact]
    public void ParsesTransactionIdAndContentLocationFromARealisticNotification()
    {
        var pdu = NotificationIndPdu();

        var result = MmsNotificationParser.Parse(pdu);

        Assert.NotNull(result);
        Assert.Equal("1234ABCD", result!.TransactionId);
        Assert.Equal("http://mmsc.carrier.example/path?id=12345", result.ContentLocation);
    }

    [Fact]
    public void ParsesEvenWhenFieldsArriveInAMinimalOrder()
    {
        // Just the three fields a download strictly needs, nothing else.
        var bytes = new List<byte> { FieldMessageType, MessageTypeNotificationInd, FieldTransactionId };
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("txn-1"));
        bytes.Add(0x00);
        bytes.Add(FieldContentLocation);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("http://example.com/m"));
        bytes.Add(0x00);

        var result = MmsNotificationParser.Parse([.. bytes]);

        Assert.NotNull(result);
        Assert.Equal("txn-1", result!.TransactionId);
        Assert.Equal("http://example.com/m", result.ContentLocation);
    }

    [Fact]
    public void HandlesALeadingQuoteOnATextStringWhoseFirstCharacterHasTheHighBitSet()
    {
        // Text-string = [Quote] *TEXT End-of-string: the Quote (0x7F) only appears when the
        // first real character's high bit is set (here, 0xC1), and is not part of the value.
        var bytes = new List<byte> { FieldMessageType, MessageTypeNotificationInd, FieldTransactionId, 0x7F, 0xC1 };
        bytes.AddRange(System.Text.Encoding.UTF8.GetBytes("XYZ"));
        bytes.Add(0x00);
        bytes.Add(FieldContentLocation);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("http://example.com/m"));
        bytes.Add(0x00);

        var result = MmsNotificationParser.Parse([.. bytes]);

        Assert.NotNull(result);
        Assert.Equal("ÁXYZ", result!.TransactionId);
    }

    [Fact]
    public void ReturnsNullForAnEmptyOrNullPdu()
    {
        Assert.Null(MmsNotificationParser.Parse(null));
        Assert.Null(MmsNotificationParser.Parse([]));
    }

    [Fact]
    public void ReturnsNullWhenTheMessageTypeIsNotANotificationInd()
    {
        var pdu = NotificationIndPdu(messageType: MessageTypeRetrieveConf);

        Assert.Null(MmsNotificationParser.Parse(pdu));
    }

    [Fact]
    public void ReturnsNullWhenTransactionIdIsMissing()
    {
        var bytes = new List<byte> { FieldMessageType, MessageTypeNotificationInd, FieldContentLocation };
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("http://example.com/m"));
        bytes.Add(0x00);

        Assert.Null(MmsNotificationParser.Parse([.. bytes]));
    }

    [Fact]
    public void ReturnsNullWhenContentLocationIsMissing()
    {
        var bytes = new List<byte> { FieldMessageType, MessageTypeNotificationInd, FieldTransactionId };
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("txn-1"));
        bytes.Add(0x00);

        Assert.Null(MmsNotificationParser.Parse([.. bytes]));
    }

    [Fact]
    public void ReturnsNullRatherThanMisreadWhenATruncatedPduRunsOutMidField()
    {
        var pdu = NotificationIndPdu();
        var truncated = pdu[..(pdu.Length - 5)]; // cuts off partway through Content-Location

        Assert.Null(MmsNotificationParser.Parse(truncated));
    }

    [Fact]
    public void StopsAtAnUnrecognisedWellKnownFieldRatherThanMisreadPastIt()
    {
        // 0xAE (CONTENT) is a well-known field this parser doesn't handle; everything
        // already read (both required fields) should still come back.
        var bytes = new List<byte> { FieldMessageType, MessageTypeNotificationInd, FieldTransactionId };
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("txn-1"));
        bytes.Add(0x00);
        bytes.Add(FieldContentLocation);
        bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("http://example.com/m"));
        bytes.Add(0x00);
        bytes.Add(0xAE); // unrecognised — the walk should stop here, not misread the rest

        var result = MmsNotificationParser.Parse([.. bytes]);

        Assert.NotNull(result);
        Assert.Equal("txn-1", result!.TransactionId);
        Assert.Equal("http://example.com/m", result.ContentLocation);
    }
}
