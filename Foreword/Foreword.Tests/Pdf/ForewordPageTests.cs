using Foreword.Domain.Brief;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// The brief's first page (B1): a near-empty page by design.
///
/// <para>The emptiness is the point — it is the page that says this was set and delivered for one
/// person, and filling it would make it a cover sheet instead. What it must carry is the name, the
/// date written out, the owner's voice, the maker's mark, and the contents, so the brief says what
/// it holds and how long it runs before a page is turned.</para>
/// </summary>
public class ForewordPageTests
{
    private static readonly DateOnly Sunday = new(2026, 9, 6);

    private static ArticleContent Article(string title, string section = "World") => new()
    {
        Title = title,
        Byline = "By Someone · The New York Times",
        Section = section,
        Body = [new TextBlock { Text = "A paragraph of the story." }],
    };

    private static byte[] Brief(string voice, params ArticleContent[] articles) =>
        new PdfComposer(NullLogger<PdfComposer>.Instance)
            .Compose(articles, Puzzle(), Sunday, voice);

    private static byte[] Brief(
        string voice, IReadOnlyList<ComicStrip> comics, HistoryNote? history,
        params ArticleContent[] articles) =>
        new PdfComposer(NullLogger<PdfComposer>.Instance)
            .Compose(articles, Puzzle(), Sunday, voice, comics, history);

    /// <summary>A one-pixel PNG, so the comic page has a real image to lay out.</summary>
    private static ComicStrip Strip() => new(3297, "OH Scale",
        "To access distant parts of the project, I built a passenger train inside each rail.",
        Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFAAH/q842iQAAAABJRU5ErkJggg=="));

    /// <summary>A stand-in for the crossword: a real two-page PDF, so the contents can count it.</summary>
    private static byte[] Puzzle()
    {
        using var layout = new SkiaPageLayout();
        layout.DrawWrapped("Puzzle page one", SkiaPageLayout.Sans(12));
        layout.NewPage();
        layout.DrawWrapped("Puzzle page two", SkiaPageLayout.Sans(12));
        return layout.ToPdf();
    }

    [Fact]
    public void TheFirstPageCarriesTheNameTheDateAndTheMark()
    {
        using var document = PdfDocument.Open(new MemoryStream(Brief("A quiet Sunday.", Article("One"))));
        var page = document.GetPage(1).Text;

        Assert.Contains("Foreword", page, StringComparison.Ordinal);
        Assert.Contains("Sunday, the sixth of September", page, StringComparison.Ordinal);
        Assert.Contains("SET AND DELIVERED BY FOREWORD", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheOwnersVoiceIsOnIt()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday, and the first one this month you could call cool.", Article("One"))));

        Assert.Contains("quiet Sunday", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// No voice, no sentence. A neutral line in the calligraphy hand would be the app impersonating
    /// them, which §4 reserves that hand against.
    /// </summary>
    [Fact]
    public void WithNoVoiceThePageSimplyHasNoSentence()
    {
        using var document = PdfDocument.Open(new MemoryStream(Brief(string.Empty, Article("One"))));
        var page = document.GetPage(1).Text;

        Assert.Contains("Foreword", page, StringComparison.Ordinal);
        Assert.DoesNotContain("quiet", page, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The contents say what the brief holds and how long it runs. The lengths are READ BACK from
    /// the composed pages rather than estimated — a contents that guessed would be wrong on exactly
    /// the mornings a story ran long.
    /// </summary>
    [Fact]
    public void TheContentsCountTheStoriesAndTheirPages()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", Article("One"), Article("Two"), Article("A column", "Opinion"))));
        var page = document.GetPage(1).Text;

        Assert.Contains("The news", page, StringComparison.Ordinal);
        Assert.Contains("2 stories", page, StringComparison.Ordinal);
        Assert.Contains("The crossword", page, StringComparison.Ordinal);
        Assert.Contains("2 pages", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The whole brief is the foreword, the stories, then the puzzle — in that order, and the puzzle
    /// last because it is the thing you do when you have finished reading.
    /// </summary>
    [Fact]
    public void TheBriefRunsForewordThenStoriesThenPuzzle()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", Article("The river towns"))));

        Assert.Equal(4, document.NumberOfPages);
        Assert.Contains("Sunday, the sixth", document.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("The river towns", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("Puzzle page one", document.GetPage(3).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A morning with no stories at all still gets its foreword and its puzzle. The brief is not
    /// only the news — and a document that lost its first page because an API was down would look
    /// like a different app.
    /// </summary>
    [Fact]
    public void AMorningWithNoStoriesStillHasAForeword()
    {
        using var document = PdfDocument.Open(new MemoryStream(Brief("A quiet Sunday.")));

        Assert.Equal(3, document.NumberOfPages);
        Assert.Contains("Sunday, the sixth", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The comic is last before the puzzle — the thing you read when you have finished reading —
    /// and its folio says so, because a reader who has just finished the news wants to know whether
    /// there is more paper coming.
    /// </summary>
    [Fact]
    public void TheComicComesAfterTheStoriesAndBeforeThePuzzle()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", [Strip()], null, Article("The river towns"))));

        Assert.Equal(5, document.NumberOfPages);
        Assert.Contains("The river towns", document.GetPage(2).Text, StringComparison.Ordinal);
        Assert.Contains("XKCD", document.GetPage(3).Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("then the crossword", document.GetPage(3).Text, StringComparison.Ordinal);
        Assert.Contains("Puzzle page one", document.GetPage(4).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The hover text is ON the page. On paper there is nothing to hover, and the hover text is the
    /// second joke — a printed xkcd without it is half a comic.
    /// </summary>
    [Fact]
    public void TheHoverTextIsPrintedUnderTheStrip()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", [Strip()], null, Article("One"))));

        Assert.Contains("passenger train inside each rail", document.GetPage(3).Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheContentsCountTheComic()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", [Strip()], null, Article("One"))));

        Assert.Contains("xkcd", document.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// No comic, no page. A brief on a morning xkcd could not be reached is shorter, not broken —
    /// and an empty keylined box would read as a strip that failed to draw.
    /// </summary>
    [Fact]
    public void NoComicMeansNoComicPage()
    {
        using var document = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", [], null, Article("One"))));

        Assert.Equal(4, document.NumberOfPages);
        Assert.DoesNotContain("XKCD", document.GetPage(2).Text, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The factoid is on the foreword page when there is one, and absent when there is not — an
    /// empty heading over a blank line is worse than no heading.
    /// </summary>
    [Fact]
    public void TheFactoidIsPrintedOnlyWhenThereIsOne()
    {
        using var with = PdfDocument.Open(new MemoryStream(Brief("A quiet Sunday.", [],
            new HistoryNote(1297, "The Battle of Stirling Bridge is fought."), Article("One"))));
        using var without = PdfDocument.Open(new MemoryStream(
            Brief("A quiet Sunday.", [], null, Article("One"))));

        Assert.Contains("Stirling Bridge", with.GetPage(1).Text, StringComparison.Ordinal);
        Assert.Contains("1297", with.GetPage(1).Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Stirling", without.GetPage(1).Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every ordinal in a month reads as a word. A "31th of March" on a printed page is the sort of
    /// thing nobody tests and everybody notices.
    /// </summary>
    [Theory]
    [InlineData(1, "first")]
    [InlineData(2, "second")]
    [InlineData(3, "third")]
    [InlineData(11, "eleventh")]
    [InlineData(12, "twelfth")]
    [InlineData(13, "thirteenth")]
    [InlineData(21, "twenty-first")]
    [InlineData(22, "twenty-second")]
    [InlineData(23, "twenty-third")]
    [InlineData(30, "thirtieth")]
    [InlineData(31, "thirty-first")]
    public void TheDateIsWrittenOutInWords(int day, string expected)
    {
        Assert.Contains($"the {expected} of", ForewordPage.WrittenOut(new DateOnly(2026, 3, day)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDayOfEveryMonthHasAnOrdinal()
    {
        for (var day = 1; day <= 31; day++)
        {
            var written = ForewordPage.WrittenOut(new DateOnly(2026, 1, day));

            Assert.DoesNotContain(day.ToString(), written, StringComparison.Ordinal);
        }
    }
}
