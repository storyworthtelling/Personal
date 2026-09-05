using Codex.Domain.Plate;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// Where a capture is filed, and under what name. This is the phase's one live bug: the
/// shipped head inserts into MediaStore with a display name and a MIME type and nothing
/// else, so the Images collection files under its default primary directory, Pictures/.
/// Google Photos automatically backs up DCIM/Camera and treats every other device folder
/// as opt-in — so every photo the app has ever taken has been sitting outside backup.
///
/// The decision lives here rather than in the Activity because the Activity cannot be
/// compiled in this sandbox at all, and "which folder" is exactly the kind of thing that
/// is easy to type wrong and impossible to notice.
/// </summary>
public class CaptureFilingTests
{
    private static readonly DateTime Noon = new(2026, 9, 4, 12, 34, 56, DateTimeKind.Local);

    [Fact]
    public void PhotosAreFiledWhereGooglePhotosActuallyBacksUp()
    {
        Assert.Equal("DCIM/Camera", CaptureFiling.Photo(Noon).RelativePath);
    }

    [Fact]
    public void VideosAreFiledInTheSameCameraRoll()
    {
        // A video the owner shot is a moment like any other; splitting it into a second
        // folder would put half of what Plate captures outside the backed-up one.
        Assert.Equal("DCIM/Camera", CaptureFiling.Video(Noon).RelativePath);
    }

    [Fact]
    public void TheNameCarriesTheAppAndASortableTimestamp()
    {
        Assert.Equal("Plate_20260904_123456.jpg", CaptureFiling.Photo(Noon).DisplayName);
        Assert.Equal("Plate_20260904_123456.mp4", CaptureFiling.Video(Noon).DisplayName);
    }

    [Fact]
    public void TheMimeTypeMatchesTheExtension()
    {
        var photo = CaptureFiling.Photo(Noon);
        var video = CaptureFiling.Video(Noon);

        Assert.Equal("image/jpeg", photo.MimeType);
        Assert.EndsWith(".jpg", photo.DisplayName, StringComparison.Ordinal);
        Assert.Equal("video/mp4", video.MimeType);
        Assert.EndsWith(".mp4", video.DisplayName, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoCapturesInTheSameSecondDoNotCollide()
    {
        // MediaStore will happily create "Plate_x (1).jpg" behind our back, which breaks
        // the sortable-name promise. A burst is the ordinary case for the Darkroom, so the
        // name has to stay unique at the resolution people actually shoot at.
        var first = CaptureFiling.Photo(Noon, sequence: 0);
        var second = CaptureFiling.Photo(Noon, sequence: 1);

        Assert.NotEqual(first.DisplayName, second.DisplayName);
        Assert.Equal("Plate_20260904_123456.jpg", first.DisplayName);
        Assert.Equal("Plate_20260904_123456_2.jpg", second.DisplayName);
    }

    [Fact]
    public void ScansAreNotFiledInTheCameraRoll()
    {
        // The other half of the live bug: the shipped head writes a scan into
        // MediaStore.Downloads, which the owner reported as "scans vanish into Downloads".
        // A scan is a document and belongs with documents — and never in the camera roll,
        // which exists for moments.
        var scan = CaptureFiling.Scan("Insurance claim", Noon);

        Assert.Equal("Documents/Plate", scan.RelativePath);
        Assert.Equal("application/pdf", scan.MimeType);
        Assert.Equal("Insurance claim — 2026-09-04.pdf", scan.DisplayName);
    }

    [Theory]
    [InlineData("a/b", "a-b")]
    [InlineData("what: now?", "what- now-")]
    [InlineData("  padded  ", "padded")]
    [InlineData("", "Scan")]
    [InlineData("   ", "Scan")]
    public void AScanTitleCannotBreakOutOfItsFolderOrItsName(string title, string expected)
    {
        // The title comes from the owner typing, so it reaches a filesystem path. A slash
        // would silently nest the file somewhere else; the reserved characters make the
        // insert fail outright on some volumes.
        Assert.Equal($"{expected} — 2026-09-04.pdf", CaptureFiling.Scan(title, Noon).DisplayName);
    }
}
