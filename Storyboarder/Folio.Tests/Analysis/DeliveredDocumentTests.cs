using Folio.Domain.Analysis;

namespace Folio.Tests.Analysis;

/// <summary>
/// What the delivered document is called, on Drive and on the tablet.
///
/// <para>It is a function rather than two interpolated strings because the two targets have to agree:
/// a report that arrives in Drive as one thing and on the reMarkable as another is two documents to
/// the person filing them, and the drift would never fail anything.</para>
///
/// <para>The word is "Revision report", not "Analysis". "Analysis" is what the pre-pivot product
/// called its output, and the rebrand swept the app's own identity while leaving the name of the
/// thing it actually hands over — the one part of it the owner sees in a folder listing.</para>
/// </summary>
public class DeliveredDocumentTests
{
    [Fact]
    public void ADocumentIsNamedForItsBookAndItsDraft() =>
        Assert.Equal("The Salt Line — Draft 3 — Revision report", DeliveredDocument.Name("The Salt Line", 3));

    [Fact]
    public void TheDriveFileIsTheSameNameWithASuffix() =>
        Assert.Equal(
            "The Salt Line — Draft 3 — Revision report.pdf",
            DeliveredDocument.FileName("The Salt Line", 3));

    /// <summary>
    /// The old word is gone from the name. A book delivered before this keeps the file it already
    /// has — nothing renames anything on Drive — but nothing new carries the pre-pivot word.
    /// </summary>
    [Fact]
    public void TheProductsOldNameIsNotInIt() =>
        Assert.DoesNotContain("Analysis", DeliveredDocument.Name("A Book", 1), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A book with no title still produces a filename. An empty name is not a file Drive will take,
    /// and a delivery that fails on the name fails after the whole review has been paid for.
    /// </summary>
    [Fact]
    public void ABookWithNoTitleIsStillDeliverable()
    {
        Assert.Equal("Untitled — Draft 1 — Revision report", DeliveredDocument.Name("   ", 1));
        Assert.Equal("Untitled — Draft 1 — Revision report", DeliveredDocument.Name(null!, 1));
    }

    /// <summary>
    /// Characters Drive and the tablet will not take in a file name come out. A slash in a title is
    /// not exotic — "Book One / Book Two" is a real way to title a volume — and on most filesystems
    /// it silently makes a folder.
    /// </summary>
    [Fact]
    public void ATitleThatCannotBeAFileNameIsMadeIntoOne() =>
        Assert.Equal(
            "Book One - Book Two — Draft 2 — Revision report.pdf",
            DeliveredDocument.FileName("Book One / Book Two", 2));
}
