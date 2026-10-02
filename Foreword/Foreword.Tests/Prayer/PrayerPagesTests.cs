using Foreword.Domain.Prayer;
using Foreword.Infrastructure.Pdf;
using UglyToad.PdfPig;
using Xunit;

namespace Foreword.Tests.Prayer;

/// <summary>
/// The two-page prayer document, opened again and read back.
///
/// <para>These sheets are composed without anyone seeing them, and the failures that produces are
/// silent: text off the trim, text under the binder tabs, a tab whose link goes nowhere. None of
/// them throws and none changes the page count.</para>
/// </summary>
public class PrayerPagesTests
{
    private static readonly DateOnly Day = new(2026, 9, 6);

    private const float PageWidth = 612f;

    private sealed class Reader : IPrayerReader
    {
    }

    private static PrayerDocument Sample() => new()
    {
        Date = Day,
        Lead = PrayerText.Of("A short list this morning, and one of them can come off it."),
        Holding =
        [
            new PrintedRequest(PrayerText.Of("Dad — the scan on the 14th"), "held 9 days", false),
            new PrintedRequest(PrayerText.Of("The Kellers, moving"), "held 3 weeks", false),
            new PrintedRequest(PrayerText.Of("Riley, settling into the new class"), "held 6 weeks", false),
            new PrintedRequest(PrayerText.Of("Sam's Boston trip"), "held 4 days", false),
            new PrintedRequest(PrayerText.Of("Work — the decision about the team"), "answered", true),
        ],
        Rotations =
        [
            new RotationPicked("Missionaries", PrayerText.Of("the Boyd family"), "2 of 9"),
        ],
        FromBefore =
        [
            new SurfacedEntry("Yesterday",
                PrayerText.Of("Asked for patience with the mornings, and got a week of them.")),
            new SurfacedEntry("A week ago",
                PrayerText.Of("Still turning over whether to say something to Dan. Not yet.")),
            new SurfacedEntry("This date, 2025",
                PrayerText.Of("First day of the new job. Asked not to be an idiot about it.")),
        ],
    };

    private static byte[] Composed() => new PrayerPages().Compose(Sample());

    [Fact]
    public void ItIsTwoPagesPortrait()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));

        Assert.Equal(2, document.NumberOfPages);
        foreach (var page in document.GetPages())
        {
            Assert.True(page.Height > page.Width, $"page {page.Number} is not portrait");
            Assert.Equal(PageWidth, page.Width, 1);
        }
    }

    [Fact]
    public void TheListPageCarriesBothHeadingsAndEveryRequest()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));
        var text = Words(document, 1);

        Assert.Contains("Holding", text);
        Assert.Contains("From before", text);
        Assert.Contains("Kellers", text);
        Assert.Contains("held 9 days", text);
        Assert.Contains("answered", text);
        Assert.Contains("This date, 2025", text);
    }

    /// <summary>
    /// A rotation prints under its own name, with its place in the list and <b>no held-for line</b>
    /// — the name was next, not carried, and a duration beside it would be invented.
    /// </summary>
    [Fact]
    public void ARotationPrintsUnderItsOwnNameWithItsPlaceInTheList()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));
        var text = Words(document, 1);

        Assert.Contains("Missionaries", text);
        Assert.Contains("Boyd", text);
        Assert.Contains("2 of 9", text);
    }

    /// <summary>
    /// The journalling page carries free ruled space and one labelled strip at the foot, and nothing
    /// else. The free space has no heading on purpose — a heading would make it a form, and what is
    /// wanted is an invitation.
    /// </summary>
    [Fact]
    public void TheJournalPageIsFreeSpaceAndOneLabelledStrip()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));
        var text = Words(document, 2);

        Assert.Contains("Journal", text);
        Assert.Contains("Foreword", text);
        Assert.Contains("Add to the list", text);

        // Nothing from the list page leaks onto it, and the free space stays unlabelled.
        Assert.DoesNotContain("Holding", text);
        Assert.DoesNotContain("From before", text);
        Assert.DoesNotContain("Kellers", text);
        Assert.DoesNotContain("Missionaries", text);
    }

    /// <summary>
    /// Both tabs on both pages, and both links real. A tab drawn without an anchor at the other end
    /// is the failure mode worth testing for, because it looks exactly like a working tab.
    /// </summary>
    [Fact]
    public void EveryTabOnEveryPageLinksToAPageThatExists()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));

        var destinations = Enum.GetValues<PrayerSection>().Select(PrayerTabs.Destination).ToArray();

        foreach (var page in document.GetPages())
        {
            var links = page.GetAnnotations().Count(annotation => annotation.Action is not null);
            Assert.Equal(destinations.Length, links);
        }
    }

    [Fact]
    public void NoTextRunsOffThePageOrUnderTheTabs()
    {
        using var document = PdfDocument.Open(new MemoryStream(Composed()));

        var stripLeft = PageWidth - Foreword.Domain.Document.SectionTabs.TabWidth
            - Foreword.Domain.Document.SectionTabs.Hairline;

        var offenders = new List<string>();

        foreach (var page in document.GetPages())
        {
            foreach (var letter in page.Letters)
            {
                var box = letter.GlyphRectangle;

                if (box.Left < 0 || box.Right > page.Width || box.Bottom < 0 || box.Top > page.Height)
                {
                    offenders.Add($"page {page.Number}: '{letter.Value}' off the sheet at {box.Left:0.#},{box.Bottom:0.#}");
                }

                // The tab labels themselves live out there; nothing else may.
                if (box.Left > stripLeft && !IsTabLabel(letter.Value))
                {
                    offenders.Add($"page {page.Number}: '{letter.Value}' under the tabs at {box.Left:0.#}");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join("\n", offenders.Take(20)));
    }

    /// <summary>
    /// A morning with nothing held and nothing written is still a document. It is the ordinary
    /// state of a first install, and the state after everything has been answered.
    /// </summary>
    [Fact]
    public void AnEmptyMorningStillComposes()
    {
        var bytes = new PrayerPages().Compose(new PrayerDocument { Date = Day });

        using var document = PdfDocument.Open(new MemoryStream(bytes));

        Assert.Equal(2, document.NumberOfPages);
        Assert.DoesNotContain("Holding", Words(document, 1));
    }

    /// <summary>
    /// And the composer reads the text through its own permission, not by way of a string that has
    /// been lying around. If <see cref="PrayerPages"/> stopped being a declared reader, this is
    /// where it would show.
    /// </summary>
    [Fact]
    public void TheComposerIsADeclaredReader()
    {
        Assert.IsAssignableFrom<IPrayerReader>(new PrayerPages());
    }

    private static bool IsTabLabel(string letter) =>
        Enum.GetValues<PrayerSection>()
            .Any(section => PrayerTabs.Label(section).Contains(letter, StringComparison.Ordinal));

    private static string Words(PdfDocument document, int pageNumber) =>
        string.Join(" ", document.GetPage(pageNumber).GetWords().Select(word => word.Text));
}
