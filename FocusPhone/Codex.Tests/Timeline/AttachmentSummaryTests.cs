using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Timeline;

public class AttachmentSummaryTests
{
    private static AttachmentRef Attachment(string mimeType) => new("id", "name", mimeType, 0);

    [Fact]
    public void NoAttachments_FallsBackToTheBarePlaceholder()
    {
        Assert.Equal("[MMS]", AttachmentSummary.Describe([]));
    }

    [Fact]
    public void OneImage_ReadsAsPhoto()
    {
        Assert.Equal("Photo", AttachmentSummary.Describe([Attachment("image/jpeg")]));
    }

    [Fact]
    public void OneVideo_ReadsAsVideo()
    {
        Assert.Equal("Video", AttachmentSummary.Describe([Attachment("video/mp4")]));
    }

    [Fact]
    public void SomethingElse_ReadsAsAttachment()
    {
        Assert.Equal("Attachment", AttachmentSummary.Describe([Attachment("audio/mpeg")]));
    }

    [Fact]
    public void MultipleParts_AppendsTheRemainingCount()
    {
        Assert.Equal("Photo +2", AttachmentSummary.Describe(
            [Attachment("image/jpeg"), Attachment("image/jpeg"), Attachment("video/mp4")]));
    }
}
