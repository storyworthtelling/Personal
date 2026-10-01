using System.Text;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// MmsSendRequestParser exists purely so MmsSendReceiver can recover what to persist as the
/// local "sent" record after a successful send — reading it back from the composed PDU file
/// rather than trusting an in-memory handoff that a killed-and-restarted process would lose.
/// These tests go straight through MmsComposer, the only producer it ever actually reads.
/// </summary>
public class MmsSendRequestParserTests
{
    private static readonly DateTimeOffset SentAt = DateTimeOffset.FromUnixTimeSeconds(1_750_000_000);

    [Fact]
    public void RecoversRecipientsSubjectAndTextPart()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-1", ["5551110000", "5552220000"], "Trip photos", "here they are", [], SentAt);

        var info = MmsSendRequestParser.Parse(pdu);

        Assert.NotNull(info);
        Assert.Equal(["5551110000", "5552220000"], info!.Recipients);
        Assert.Equal("Trip photos", info.Subject);
        var part = Assert.Single(info.Parts);
        Assert.Equal("text/plain", part.MimeType);
        Assert.Equal("here they are", Encoding.UTF8.GetString(part.Data));
    }

    [Fact]
    public void RecoversMediaPartsWithFileNameAndBytesIntact()
    {
        var photo = new MmsOutboundPart("photo.jpg", "image/jpeg", [1, 2, 3, 4]);
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-2", ["5551234567"], subject: null, text: "look", media: [photo], SentAt);

        var info = MmsSendRequestParser.Parse(pdu);

        Assert.NotNull(info);
        Assert.Equal(2, info!.Parts.Count);
        var recoveredPhoto = info.Parts[1];
        Assert.Equal("photo.jpg", recoveredPhoto.FileName);
        Assert.Equal("image/jpeg", recoveredPhoto.MimeType);
        Assert.Equal(photo.Data, recoveredPhoto.Data);
    }

    [Fact]
    public void NoSubject_ParsesAsNull()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-3", ["5551234567"], subject: null, text: "x", media: [], SentAt);

        Assert.Null(MmsSendRequestParser.Parse(pdu)!.Subject);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(new byte[0])]
    public void NullOrEmptyPdu_ReturnsNull(byte[]? pdu)
    {
        Assert.Null(MmsSendRequestParser.Parse(pdu));
    }

    [Fact]
    public void TruncatedPdu_ReturnsNullRatherThanThrowing()
    {
        var pdu = MmsComposer.ComposeSendRequest(
            "txn-4", ["5551234567"], "subject line", "text body", [], SentAt);

        Assert.Null(MmsSendRequestParser.Parse(pdu[..(pdu.Length - 5)]));
    }
}
