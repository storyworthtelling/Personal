using Folio.Domain.Series;
using Folio.Infrastructure.Imaging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using Xunit;

namespace Folio.Tests.Report;

// The series bible printed as its own document (release G, R26), with an invented series.
public class SeriesBibleDocumentTests
{
    private static readonly Guid BookOne = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid BookTwo = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static SeriesBible Bible() => SeriesBible.Empty
        .WithBook(new SeriesBook(BookOne, "The Salt Road", 1),
        [
            new BibleFact(BibleKind.Character, "Wren Abernay", ["the pilot"], "Year of the flood", "A ferry pilot with a scarred left hand.", "her scarred left hand"),
            new BibleFact(BibleKind.Setting, "Gullhaven", [], "", "A harbour town built on stilts.", ""),
            new BibleFact(BibleKind.Thread, "The lost ledger", [], "", "The ledger went down with the ferry.", ""),
        ])
        .WithBook(new SeriesBook(BookTwo, "The Tide Ledger", 2),
            [new BibleFact(BibleKind.Character, "Wren Abernay", [], "Ten years on", "Now the harbourmaster.", "")])
        .Correct(BibleKind.Setting, "Gullhaven", "A harbour town built on piles, not stilts.");

    private static List<string> Pages(byte[] pdf)
    {
        using var document = PdfDocument.Open(new MemoryStream(pdf));
        return [.. document.GetPages().Select(page => page.Text.Replace(" ", string.Empty, StringComparison.Ordinal))];
    }

    [Fact]
    public void TheBiblePrintsEveryEntryWithItsBooksQuotesAndCorrections()
    {
        var pdf = SeriesBibleDocument.Render("The Tides", Bible(),
            new Dictionary<Guid, StoryPlacement> { [BookTwo] = StoryPlacement.After("The Salt Road").Confirm() });

        var text = string.Concat(Pages(pdf));
        Assert.Contains("TheTides", text, StringComparison.Ordinal);
        Assert.Contains("TheSaltRoad", text, StringComparison.Ordinal);
        Assert.Contains("TakesplaceafterTheSaltRoad", text.Replace("“", "").Replace("”", ""), StringComparison.Ordinal);
        Assert.Contains("WrenAbernay", text, StringComparison.Ordinal);
        Assert.Contains("thepilot", text, StringComparison.Ordinal);
        Assert.Contains("Yearoftheflood", text, StringComparison.Ordinal);
        Assert.Contains("herscarredlefthand", text, StringComparison.Ordinal);
        Assert.Contains("Nowtheharbourmaster.", text, StringComparison.Ordinal);
        Assert.Contains("builtonpiles,notstilts.", text, StringComparison.Ordinal);
        Assert.Contains("Openthreads", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Worldrules", text, StringComparison.Ordinal);
    }

    [Fact]
    public void EachKindThatPrintsHasATabLinkedToWhereItStarts()
    {
        var pdf = SeriesBibleDocument.Render("The Tides", Bible(), new Dictionary<Guid, StoryPlacement>());

        using var document = PdfDocument.Open(new MemoryStream(pdf));
        var links = document.GetPage(1).GetAnnotations()
            .Select(annotation => annotation.Action)
            .OfType<AbstractGoToAction>()
            .ToList();
        Assert.Equal(3, links.Count);
    }

    [Fact]
    public void AnEmptyBiblePrintsNothing() =>
        Assert.Null(SeriesBibleDocument.RenderOrNull("The Tides", SeriesBible.Empty, new Dictionary<Guid, StoryPlacement>()));
}
