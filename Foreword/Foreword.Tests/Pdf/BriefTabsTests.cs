using Foreword.Domain.Brief;
using Foreword.Domain.Models;
using Foreword.Infrastructure.Pdf;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Actions;
using UglyToad.PdfPig.Annotations;
using Xunit;

namespace Foreword.Tests.Pdf;

/// <summary>
/// The morning brief's binder tabs (owner's decision): News, Opinion, Newsletters, Comics and Games, in that order, an
/// open list a new kind of paper joins by name. A section has a tab only on a morning it has pages, every page Foreword
/// draws carries the whole strip, and each tab lands on its section's first page — the crossword's included, though its
/// own pages are the paper's and carry no strip.
/// </summary>
public class BriefTabsTests
{
    private static ArticleContent Story(string section, string title) => new()
    {
        Title = title,
        Byline = "By Sam Lee · The New York Times",
        Section = section,
        Body = [new TextBlock { Text = "One paragraph of invented copy for a story nobody wrote." }],
    };

    private static byte[] Png()
    {
        using var bitmap = new SKBitmap(40, 20);
        bitmap.Erase(SKColors.Gray);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private static byte[] Puzzle(int pages)
    {
        using var puzzle = new SkiaPageLayout();
        for (var i = 0; i < pages; i++)
        {
            if (i > 0)
            {
                puzzle.NewPage();
            }
            puzzle.DrawCentered($"crossword {i + 1}", SkiaPageLayout.Serif(12), 20);
        }
        return puzzle.ToPdf();
    }

    private static byte[] Brief(bool crossword = true, bool comic = true) =>
        new PdfComposer(NullLogger<PdfComposer>.Instance).Compose(
            [Story("Opinion", "An invented argument"), Story("World", "An invented flood"), Story("Business", "An invented foundry")],
            crossword ? Puzzle(2) : [],
            new DateOnly(2026, 9, 6),
            "An invented morning.",
            comic ? [new ComicStrip(1, "Invented strip", "Alt text.", Png())] : []);

    [Fact]
    public void TheSectionsAreAnOpenListInTheOwnersOrder()
    {
        Assert.Equal(["News", "Opinion", "Newsletters", "Comics", "Games"], BriefTabs.All.Select(tab => tab.Label));
        Assert.Equal(BriefTabs.All.Count, BriefTabs.All.Select(tab => tab.Destination).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EveryPageForewordDrewCarriesATabForEachSectionThatHasPages()
    {
        using var pdf = PdfDocument.Open(Brief());
        var pages = pdf.GetPages().ToList();

        // Cover, two news stories, one opinion, one comic, then the paper's two crossword pages.
        Assert.Equal(7, pages.Count);
        foreach (var page in pages.Take(5))
        {
            var labels = string.Concat(page.Letters.Where(l => Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > 0.01).Select(l => l.Value));
            Assert.Equal(4, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
            Assert.Contains("News", labels);
            Assert.Contains("Opinion", labels);
            Assert.Contains("Comics", labels);
            Assert.Contains("Games", labels);
            Assert.DoesNotContain("Newsletters", labels);
        }
        Assert.All(pages.Skip(5), page => Assert.Empty(page.GetAnnotations()));
    }

    [Fact]
    public void EachTabLandsOnItsSectionsFirstPage()
    {
        using var pdf = PdfDocument.Open(Brief());
        var expected = new Dictionary<string, int> { ["News"] = 2, ["Opinion"] = 4, ["Comics"] = 5, ["Games"] = 6 };

        foreach (var page in pdf.GetPages().Take(5))
        {
            var targets = page.GetAnnotations()
                .Where(a => a.Type == AnnotationType.Link)
                .OrderByDescending(a => a.Rectangle.Top)
                .Select(a => Assert.IsAssignableFrom<AbstractGoToAction>(a.Action).Destination.PageNumber)
                .ToList();
            Assert.Equal(expected.Values.ToList(), targets);
        }
    }

    [Fact]
    public void WithNoCrosswordThereIsNoGamesTab()
    {
        using var pdf = PdfDocument.Open(Brief(crossword: false, comic: false));

        foreach (var page in pdf.GetPages())
        {
            Assert.Equal(2, page.GetAnnotations().Count(a => a.Type == AnnotationType.Link));
        }
    }
}
