using Folio.Domain.Text;
using Xunit;

namespace Folio.Tests.Text;

public class ManuscriptTitleTests
{
    [Theory]
    [InlineData("Gathering Storm.docx", "Gathering Storm")]
    [InlineData("Gathering Storm.DOCX", "Gathering Storm")]
    [InlineData("My Book.gdoc", "My Book")]
    [InlineData("Draft.rtf", "Draft")]
    [InlineData("Novel.epub", "Novel")]
    [InlineData("Manuscript.pdf", "Manuscript")]
    [InlineData("Notes.txt", "Notes")]
    public void StripsAKnownManuscriptExtension(string fileName, string expected)
    {
        Assert.Equal(expected, ManuscriptTitle.FromFileName(fileName));
    }

    [Fact]
    public void KeepsNativeGoogleDocNameThatHasNoExtension()
    {
        Assert.Equal("Gathering Storm", ManuscriptTitle.FromFileName("Gathering Storm"));
    }

    [Fact]
    public void DoesNotStripADotThatIsNotAKnownExtension()
    {
        // "Book 2.5" must survive intact — ".5" is not a manuscript extension.
        Assert.Equal("Book 2.5", ManuscriptTitle.FromFileName("Book 2.5"));
    }

    [Fact]
    public void TrimsSurroundingWhitespace()
    {
        Assert.Equal("Spaced Out", ManuscriptTitle.FromFileName("  Spaced Out.docx  "));
    }

    [Fact]
    public void FallsBackToRawInputWhenNameIsOnlyAnExtension()
    {
        Assert.Equal(".docx", ManuscriptTitle.FromFileName(".docx"));
    }
}
