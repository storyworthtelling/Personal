using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

// Efficiency audit §7: every MMS row cost three more provider queries (its text, its addresses twice over, its
// attachments), so a long MMS history cost thousands. The parts are now read in one query for the whole table and
// grouped here, and each message's addresses in one query instead of two.
public class MmsRowsTests
{
    [Fact]
    public void PartsAreGroupedByMessage()
    {
        var grouped = MmsRows.GroupParts(
        [
            new MmsStoredPart("1", "10", "text/plain", "Running late", false, null, null, null),
            new MmsStoredPart("1", "11", "application/smil", "<smil/>", false, null, null, null),
            new MmsStoredPart("1", "12", "image/jpeg", null, true, "photo.jpg", null, null),
            new MmsStoredPart("2", "20", "video/mp4", null, true, null, "clip.mp4", null),
        ]);

        Assert.Equal("Running late", grouped["1"].Text);
        var photo = Assert.Single(grouped["1"].Attachments);
        Assert.Equal("mms-part:12", photo.Id);
        Assert.Equal("photo.jpg", photo.FileName);
        Assert.Equal("image/jpeg", photo.MimeType);
        Assert.Null(grouped["2"].Text);
        Assert.Equal("clip.mp4", Assert.Single(grouped["2"].Attachments).FileName);
    }

    [Fact]
    public void TextKeptInAFileIsNotRead()
    {
        var grouped = MmsRows.GroupParts([new MmsStoredPart("1", "10", "text/plain", "ignored", true, null, null, null)]);

        Assert.Null(grouped["1"].Text);
    }

    [Fact]
    public void AMessageWithNoPartsHasNothing()
    {
        Assert.Equal(MmsContent.Empty, MmsRows.ContentOf(MmsRows.GroupParts([]), "9"));
    }

    [Fact]
    public void TheCounterpartIsTheSenderOfAnIncomingMessage()
    {
        var (counterpart, all) = MmsRows.Addresses(
        [
            new MmsStoredAddress("+15550100", "151"),
            new MmsStoredAddress("+15550101", "137"),
            new MmsStoredAddress("insert-address-token", "137"),
            new MmsStoredAddress("", "130"),
        ], messageBox: 1);

        Assert.Equal("+15550101", counterpart);
        Assert.Equal(["+15550100", "+15550101"], all);
    }

    [Fact]
    public void TheCounterpartIsTheRecipientOfASentMessage()
    {
        var (counterpart, _) = MmsRows.Addresses(
        [
            new MmsStoredAddress("insert-address-token", "137"),
            new MmsStoredAddress("+15550102", "151"),
        ], messageBox: 2);

        Assert.Equal("+15550102", counterpart);
    }
}
