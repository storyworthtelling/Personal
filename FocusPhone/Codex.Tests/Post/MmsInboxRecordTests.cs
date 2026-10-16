using System.Text;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// What a downloaded MMS becomes in the provider: the rows SmsLogReader reads back. The
// head turns these into ContentValues one for one; the decisions are made here.
public class MmsInboxRecordTests
{
    private static readonly DateTimeOffset Received = new(2026, 10, 16, 14, 0, 0, TimeSpan.Zero);

    private static RetrievedPart Part(string type, byte[] data, int charset = 0, string? name = null) =>
        new(type, charset, name, name, null, data);

    private static RetrievedMms Message(params RetrievedPart[] parts) => new(
        TransactionId: "T1",
        MessageId: "MSG-1@mmsc.example",
        From: "+15550100001",
        To: ["+15550100002", "+15550100009"],
        Cc: ["+15550100003"],
        Subject: "Weekend",
        DateSeconds: Received.AddMinutes(-3).ToUnixTimeSeconds(),
        MmsVersion: 0x12,
        RetrieveStatus: null,
        ContentType: "application/vnd.wap.multipart.related",
        Parts: parts);

    [Fact]
    public void DatesTheRowWhenItArrived_AndKeepsWhenItWasSent()
    {
        var record = MmsInboxRecord.From(Message(), Received);

        Assert.Equal(Received.ToUnixTimeSeconds(), record.DateSeconds);
        Assert.Equal(Received.AddMinutes(-3).ToUnixTimeSeconds(), record.DateSentSeconds);
    }

    [Fact]
    public void ListsEveryAddressWithItsProviderType()
    {
        var record = MmsInboxRecord.From(Message(), Received);

        Assert.Equal(
            [
                new MmsAddressRow("+15550100001", MmsInboxRecord.AddressFrom),
                new MmsAddressRow("+15550100002", MmsInboxRecord.AddressTo),
                new MmsAddressRow("+15550100009", MmsInboxRecord.AddressTo),
                new MmsAddressRow("+15550100003", MmsInboxRecord.AddressCc),
            ],
            record.Addresses);
        Assert.Equal(137, MmsInboxRecord.AddressFrom);
        Assert.Equal(151, MmsInboxRecord.AddressTo);
        Assert.Equal(130, MmsInboxRecord.AddressCc);
    }

    // Text and SMIL live in the part row's own text column; the provider refuses a file for
    // them. Everything else is bytes written to the part's file.
    [Fact]
    public void TextIsInline_PicturesAreData()
    {
        var record = MmsInboxRecord.From(Message(
            Part("application/smil", Encoding.ASCII.GetBytes("<smil/>")),
            Part("text/plain", Encoding.UTF8.GetBytes("Café"), charset: 0x6A),
            Part("image/jpeg", [1, 2, 3], name: "IMG.jpg")), Received);

        Assert.Equal("<smil/>", record.Parts[0].Text);
        Assert.Null(record.Parts[0].Data);
        Assert.Equal("Café", record.Parts[1].Text);
        Assert.Equal([1, 2, 3], record.Parts[2].Data);
        Assert.Null(record.Parts[2].Text);
        Assert.Equal("IMG.jpg", record.Parts[2].Name);
        Assert.False(record.TextOnly);
    }

    [Fact]
    public void TextIsStoredAsUtf8WhateverItArrivedIn()
    {
        var record = MmsInboxRecord.From(Message(
            Part("text/plain", Encoding.BigEndianUnicode.GetBytes("Hi"), charset: 0x03E8)), Received);

        Assert.Equal("Hi", record.Parts[0].Text);
        Assert.Equal(0x6A, record.Parts[0].Charset);
    }

    // There is no image/jpg; Android's own persister corrects it, and so does this.
    [Fact]
    public void ImageJpg_IsStoredAsImageJpeg()
    {
        var record = MmsInboxRecord.From(Message(Part("IMAGE/JPG", [1])), Received);

        Assert.Equal("image/jpeg", record.Parts[0].ContentType);
    }

    [Fact]
    public void SmilAndText_IsTextOnly()
    {
        var record = MmsInboxRecord.From(Message(
            Part("application/smil", [0x3C]),
            Part("text/plain", [0x41])), Received);

        Assert.True(record.TextOnly);
        Assert.Equal("A", record.Text);
    }

    [Fact]
    public void Text_IsEveryTextPartJoined()
    {
        var record = MmsInboxRecord.From(Message(
            Part("text/plain", "first"u8.ToArray()),
            Part("image/png", [1]),
            Part("text/plain", "second"u8.ToArray())), Received);

        Assert.Equal("first\nsecond", record.Text);
    }

    [Fact]
    public void ASenderlessMessage_IsStillStored()
    {
        var message = Message(Part("text/plain", [0x41])) with { From = null };

        var record = MmsInboxRecord.From(message, Received);

        Assert.DoesNotContain(record.Addresses, row => row.Type == MmsInboxRecord.AddressFrom);
    }
}
