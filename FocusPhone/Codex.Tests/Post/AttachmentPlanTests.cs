using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// What Post does with an attachment, decided away from the device. Post is not a file
/// manager — the owner's rule — so an attachment is VIEWED here or handed to the app whose
/// job it is, and never accumulated.
/// </summary>
public class AttachmentPlanTests
{
    private static AttachmentRef File(string name, string mime, long bytes = 1024) =>
        new(Id: name, FileName: name, MimeType: mime, SizeBytes: bytes);

    // An image opens in Plate, which is the photo app. Handing a picture to a generic
    // viewer when the suite has its own is the sort of seam the whole phone exists to avoid.
    [Fact]
    public void APictureGoesToPlate()
    {
        Assert.Equal(AttachmentAction.OpenInPlate, AttachmentPlan.For(File("dog.jpg", "image/jpeg")));
    }

    // A PDF is a document: the reMarkable is where documents are read, but nothing on the
    // phone can render one, so it hands off to whatever can.
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    public void ADocumentIsHandedOff(string mime)
    {
        Assert.Equal(AttachmentAction.HandOff, AttachmentPlan.For(File("report", mime)));
    }

    // Text can simply be read where it is, which is the one case Post can answer itself.
    [Theory]
    [InlineData("text/plain")]
    [InlineData("text/calendar")]
    public void TextIsReadInPlace(string mime)
    {
        Assert.Equal(AttachmentAction.ShowInline, AttachmentPlan.For(File("note.txt", mime)));
    }

    // An executable is not something a phone with no sideloading should be offering to
    // open, and a stranger chose both the name and the type.
    [Theory]
    [InlineData("application/vnd.android.package-archive")]
    [InlineData("application/x-msdownload")]
    [InlineData("application/octet-stream")]
    public void AnExecutableIsRefused(string mime)
    {
        Assert.Equal(AttachmentAction.Refuse, AttachmentPlan.For(File("thing", mime)));
    }

    // The extension is a stranger's choice too, so a mismatch is treated as the more
    // dangerous of the two rather than the more convenient.
    [Fact]
    public void AMismatchedExtensionIsRefused()
    {
        Assert.Equal(AttachmentAction.Refuse, AttachmentPlan.For(File("invoice.pdf.apk", "image/jpeg")));
    }

    // Something enormous is not opened on a phone without saying so first.
    [Fact]
    public void SomethingHugeIsHandedOffRatherThanOpened()
    {
        Assert.Equal(AttachmentAction.HandOff,
            AttachmentPlan.For(File("holiday.jpg", "image/jpeg", AttachmentPlan.InlineLimitBytes + 1)));
    }

    // A label the owner can read, not a MIME type.
    [Theory]
    [InlineData(1, "1 attachment")]
    [InlineData(3, "3 attachments")]
    public void TheLabelCountsInWords(int count, string expected)
    {
        var files = Enumerable.Range(0, count).Select(i => File($"f{i}", "image/jpeg")).ToList();

        Assert.Equal(expected, AttachmentPlan.Label(files));
    }
}
