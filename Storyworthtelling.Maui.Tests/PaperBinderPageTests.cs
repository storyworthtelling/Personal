using System.Text.RegularExpressions;

namespace Storyworthtelling.Maui.Tests;

/// <summary>
/// One binder page for every MAUI head (audit W8.3a, report 14 §1–9). Index, Folio and Foreword each
/// built their own copy of the header, well, strip and bar, and the copies drifted: the strip stopped
/// above the bar in two and left a paper hole beside it in the third, the primary button led in one and
/// trailed in two, the bar had three paddings, and the dog-ear sat in three different places.
///
/// <para>These read the kit's source because the kit needs the Android workload to build; the heads
/// prove it compiles.</para>
/// </summary>
public partial class PaperBinderPageTests
{
    private static readonly string[] Heads =
    [
        "Ledger/Ledger.Android/Pages/LedgerBinderPage.cs",
        "Storyboarder/Folio.Android/Pages/FolioBinderPage.cs",
        "Foreword/Foreword.Android/Pages/BinderPage.cs",
    ];

    [Fact]
    public void EveryHeadsBinderPageIsTheKitsPage()
    {
        foreach (var head in Heads)
        {
            var source = File.ReadAllText(RepoFile(head));
            Assert.Matches(BaseIsTheKit(), source);
            // The frame's own furniture lives in the kit; a head that grows its own strip or bar again
            // is the drift this replaced.
            Assert.DoesNotContain("new BinderTabs()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("new PaperPageHeader()", source, StringComparison.Ordinal);
            Assert.DoesNotContain("barSlot", source, StringComparison.Ordinal);
        }
    }

    /// <summary>The owner's ruling: the binder strip runs to the foot of the screen; the bar sits under the page.</summary>
    [Fact]
    public void TheStripRunsToTheFootAndTheBarSitsUnderThePageOnly()
    {
        var page = Kit();

        Assert.Contains("Grid.SetRowSpan(strip, 2);", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid.SetColumnSpan(barSlot", page, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(barSlot, 0);", page, StringComparison.Ordinal);
    }

    /// <summary>§7: the primary is last, on the right, and takes the width that is left.</summary>
    [Fact]
    public void ThePrimaryIsLastAndWidest()
    {
        Assert.Contains("i == Buttons.Count - 1 ? GridLength.Star : GridLength.Auto", Bar(), StringComparison.Ordinal);
        Assert.Contains("PaperBar.Of(buttons)", Kit(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheBarIsPaddedFromTheBarTokens()
    {
        Assert.Contains("DesignTokens.Shape.BarPaddingX", Bar(), StringComparison.Ordinal);
        Assert.Contains("DesignTokens.Shape.BarPaddingY", Bar(), StringComparison.Ordinal);
    }

    /// <summary>The dog-ear's place is the frame's: the page's own top corner, beside the binder.</summary>
    [Fact]
    public void TheFrameOwnsTheDogEarCorner()
    {
        var page = Kit();
        Assert.Contains("cornerSlot", page, StringComparison.Ordinal);
        Assert.Contains("protected void SetCorner(DogEar ear, DogEarState state)", page, StringComparison.Ordinal);

        // Foreword's Today folded its corner into the scrolling content, under the status line.
        var today = File.ReadAllText(RepoFile("Foreword/Foreword.Android/Pages/MainPage.xaml"));
        Assert.DoesNotContain("NoticeCorner", today, StringComparison.Ordinal);
    }

    /// <summary>The Waiting page is drawn in the app's own frame in every head, not the bare kit page.</summary>
    [Fact]
    public void NoHeadOpensTheUnframedWaitingPage()
    {
        foreach (var folder in new[] { "Ledger/Ledger.Android", "Storyboarder/Folio.Android", "Foreword/Foreword.Android" })
        {
            var hits = Directory.EnumerateFiles(RepoFile(folder), "*.cs", SearchOption.AllDirectories)
                .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                .Where(file => File.ReadAllText(file).Contains("new WaitingPage(", StringComparison.Ordinal))
                .ToList();
            Assert.True(hits.Count == 0, "Opens the unframed Waiting page: " + string.Join(", ", hits));
        }
    }

    /// <summary>
    /// The well's padding is the frame's (24 at the sides, nothing at the top under the header's rule),
    /// carried by the one ScreenPadding token every page uses rather than a thickness of its own.
    /// </summary>
    [Fact]
    public void ScreenPaddingIsSidesAndFootNotTop()
    {
        var dictionary = File.ReadAllText(RepoFile("Storyworthtelling.Maui/Resources/Styles/Paper.xaml"));
        Assert.Contains("<Thickness x:Key=\"ScreenPadding\">24,0,24,24</Thickness>", dictionary, StringComparison.Ordinal);
    }

    /// <summary>§9's 26dp tab glyph, one token in both kits (the MAUI strip drew 24).</summary>
    [Fact]
    public void TheTabGlyphIsTwentySixInBothKits()
    {
        Assert.Equal(26, DesignTokens.Shape.TabGlyphDp);
        Assert.Contains("DesignTokens.Shape.TabGlyphDp", File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/BinderTabs.cs")), StringComparison.Ordinal);
        Assert.Contains("DesignTokens.Shape.TabGlyphDp", File.ReadAllText(RepoFile("FocusPhone/Codex.Ui.Android/BinderTabs.cs")), StringComparison.Ordinal);
    }

    [GeneratedRegex(@"abstract class \w+ : PaperBinderPage\b")]
    private static partial Regex BaseIsTheKit();

    /// <summary>No page draws its own bar: a hairline over a hand-padded row is the kit's PaperBar or SetBar.</summary>
    [Fact]
    public void NoPageDrawsItsOwnBar()
    {
        var own = new Regex(@"<BoxView HeightRequest=""1"" Color=""\{StaticResource Hairline\}"" />\s*<(?:Grid|HorizontalStackLayout)[^>]*Padding=""24,12""");
        var offenders = new[] { "Ledger/Ledger.Android", "Storyboarder/Folio.Android", "Foreword/Foreword.Android" }
            .SelectMany(folder => Directory.EnumerateFiles(RepoFile(folder), "*.xaml", SearchOption.AllDirectories))
            .Where(file => own.IsMatch(File.ReadAllText(file)))
            .ToList();
        Assert.True(offenders.Count == 0, "Draws its own bar: " + string.Join(", ", offenders));
    }

    private static string Bar() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/PaperBar.cs"));

    private static string Kit() => File.ReadAllText(RepoFile("Storyworthtelling.Maui/Views/PaperBinderPage.cs"));

    private static string RepoFile(string relative) =>
        Path.Combine(RepoRoot(), relative.Replace('/', Path.DirectorySeparatorChar));

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, ".github")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory!.FullName;
    }
}
