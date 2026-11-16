using Folio.Domain.Series;
using Xunit;

namespace Folio.Tests.Series;

// The series bible as the versioned Google Doc in Drive: plain text, in a stable order, so the doc's
// version history shows what each book and each correction changed (invented series).
public class BibleDocumentTextTests
{
    private static readonly Guid BookOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static SeriesBible Bible() => SeriesBible.Empty
        .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
        [
            new BibleFact(BibleKind.Setting, "Gullhaven", [], "", "On stilts.", ""),
            new BibleFact(BibleKind.Character, "Wren Abernay", ["the pilot"], "Year of the flood", "A ferry pilot.", "her scarred left hand"),
        ])
        .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2),
            [new BibleFact(BibleKind.Character, "Wren Abernay", [], "", "Now the harbourmaster.", "")])
        .Correct(BibleKind.Setting, "Gullhaven", "On piles.");

    [Fact]
    public void TheDocumentSaysWhatPromptedThisVersionThenTheBooksThenEveryEntry()
    {
        var text = BibleDocumentText.For("The Tides", Bible(),
            new Dictionary<Guid, StoryPlacement> { [BookTwo] = StoryPlacement.After("The Salt Road").Confirm() },
            "after The Tide Ledger, draft 2");

        var lines = text.Split('\n');
        Assert.Equal("The Tides — Series bible", lines[0]);
        Assert.Equal("This version: after The Tide Ledger, draft 2", lines[1]);
        Assert.Contains("1. The Salt Road", text);
        Assert.Contains("2. The Tide Ledger — Takes place after “The Salt Road”", text);
        Assert.Contains("Wren Abernay (also the pilot)", text);
        Assert.Contains("  The Salt Road (Year of the flood): A ferry pilot.", text);
        Assert.Contains("    “her scarred left hand”", text);
        Assert.Contains("  The Tide Ledger: Now the harbourmaster.", text);
        Assert.Contains("  Your correction: On piles.", text);
        // Characters come before settings whatever order they were read in.
        Assert.True(text.IndexOf("CHARACTERS", StringComparison.Ordinal) < text.IndexOf("SETTINGS", StringComparison.Ordinal));
        Assert.DoesNotContain("WORLD RULES", text);
    }

    /// <summary>
    /// Only what changed moves between versions: the same bible written twice, for different
    /// occasions, differs in its second line alone.
    /// </summary>
    [Fact]
    public void TheSameBibleWrittenAgainDiffersOnlyInWhatPromptedIt()
    {
        var none = new Dictionary<Guid, StoryPlacement>();
        var first = BibleDocumentText.For("The Tides", Bible(), none, "after The Tide Ledger, draft 2").Split('\n');
        var second = BibleDocumentText.For("The Tides", Bible(), none, "your correction to Gullhaven").Split('\n');

        Assert.Equal(first.Length, second.Length);
        Assert.Equal([1], Enumerable.Range(0, first.Length).Where(i => first[i] != second[i]));
    }
}
