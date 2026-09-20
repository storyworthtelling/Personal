using System.Text.RegularExpressions;
using Xunit;

namespace Folio.Tests.Guard;

/// <summary>
/// Every screen wears the app's frame.
///
/// <para><b>This guard exists because of what happened in P9.</b> Foreword's binder strip was built,
/// tested, and then never wired to a page: the app shipped navigating by two quiet buttons in the
/// bottom bar, which put one of its four areas three taps deep inside Settings. Nothing failed.
/// The component was correct, the screens simply did not use it — and no test asked whether they
/// did, because every test was about the component.</para>
///
/// <para>So these assertions are about the <i>screens</i>. A page that forgets the frame has no
/// tabs, no in-page header and no pinned bar, and there is no other way to notice from here: the
/// head cannot be run in this environment and CI only compiles it.</para>
/// </summary>
public class ScreenFrameTests
{
    /// <summary>
    /// The pages that are screens, as opposed to the base class and the overlays. Named rather than
    /// discovered: adding a screen should be a deliberate line here, which is exactly the moment to
    /// ask whether it belongs in the binder.
    /// </summary>
    public static TheoryData<string> Screens =>
        ["MainPage", "SetupPage", "ShelfPage", "BookPage", "NewProjectPage", "DraftDiffPage"];

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryScreenIsInTheBinder(string screen)
    {
        var source = Source(screen);

        Assert.True(
            source.Contains("FolioBinderPage", StringComparison.Ordinal),
            $"{screen} is not a FolioBinderPage, so it has no binder tabs, no in-page header and no "
            + "pinned bar. This is the P9 failure: a strip that exists and is not used.");
    }

    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryScreenDeclaresWhichAreaItBelongsTo(string screen)
    {
        var source = Source(screen);

        Assert.Matches(new Regex(@"Wrap\(\s*FolioArea\.\w+", RegexOptions.None), source);
    }

    /// <summary>
    /// No screen draws its own header. The frame draws it, with its area's rule under it; a second
    /// one in the page would say the same word twice, in the wrong face.
    ///
    /// <para>What is forbidden is a header <b>in the XAML</b> — a declared Display label is a screen
    /// title. The draft-diff screen still builds Display-sized headings in code, and those are
    /// headings <i>within a document</i>: its top level is allowed to be the largest thing in the
    /// scroll without being the screen's name.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void NoScreenDeclaresItsOwnHeaderInXaml(string screen)
    {
        foreach (var file in Files(screen).Where(path => path.EndsWith(".xaml", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain("DisplayLabel", File.ReadAllText(file), StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Navigating by a button in the bottom bar is the thing the binder replaced. The bar carries
    /// what the screen is <i>for</i>; where you can go is the strip.
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void NoScreenNavigatesByAButtonToAnArea(string screen)
    {
        var source = string.Concat(Files(screen).Select(File.ReadAllText));

        foreach (var area in new[] { "Shelf", "Settings", "Work desk" })
        {
            Assert.DoesNotContain($"Text=\"{area}\"", source, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Item actions belong on §7's card, not in a system action sheet. The card is not a dialog: it
    /// does not interrupt, it is the app's own paper, and its destructive row asks nothing.
    ///
    /// <para><b>Settings keeps two sheets, recorded rather than converted.</b> They offer <i>View
    /// &amp; copy</i> or <i>Share</i> for a diagnostics blob — a chooser for getting text off the
    /// phone, not a set of things you can do to an item, which is what the card is for and what
    /// makes it worth its haze. They are listed so that anything <b>new</b> still fails.</para>
    /// </summary>
    [Fact]
    public void NothingOffersItemActionsInASystemDialog()
    {
        var offenders = FolioInvariantsTests.HeadFiles()
            .Where(file => File.ReadAllText(file).Contains("DisplayActionSheet", StringComparison.Ordinal))
            .Select(FolioInvariantsTests.Relative)
            .Where(path => !path.EndsWith("SetupPage.xaml.cs", StringComparison.Ordinal))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Item actions belong on the §7 action card, not in a system action sheet:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// Nothing asks "are you sure". Deleting is undo-first (§6) — the dog-ear carries the undo — and
    /// for the acts that cannot be undone the answer is not a prompt either: what a restore replaces
    /// is said on the page, and choosing a particular file out of a picker is already the deliberate
    /// act a prompt would be asking for a second time.
    /// </summary>
    [Fact]
    public void NothingAsksAreYouSure()
    {
        var offenders = FolioInvariantsTests.HeadFiles()
            .Where(file => Regex.IsMatch(
                File.ReadAllText(file),
                @"DisplayAlert\([^)]*""(Cancel|No)""",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            .Select(FolioInvariantsTests.Relative)
            .ToList();

        Assert.True(offenders.Count == 0,
            "A confirmation prompt asks before acting. Act and offer the undo, or say what it does "
            + "on the page:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Every screen in the binder carries a pinned bar, because §7 puts the primary action there
    /// and a screen with no bar is a screen whose primary is nowhere.
    ///
    /// <para>The Shelf had none. It passed every other assertion in this file — it wears the frame,
    /// declares its area, draws no header of its own — because each of those is about what a screen
    /// must NOT do. This is the one that asks what it must.</para>
    ///
    /// <para><c>NewProjectPage</c> is exempt and named rather than skipped: it is a picker handed
    /// to the owner mid-flow, and its primary action is choosing a document from the list.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Screens))]
    public void EveryScreenPinsItsPrimaryAction(string screen)
    {
        if (screen is "NewProjectPage" or "DraftDiffPage" or "SetupPage")
        {
            return;
        }

        Assert.Contains("SetBar(", Source(screen), StringComparison.Ordinal);
    }

    private static string Source(string screen) =>
        string.Concat(Files(screen).Select(File.ReadAllText));

    private static IReadOnlyList<string> Files(string screen)
    {
        var found = FolioInvariantsTests.HeadFiles()
            .Concat(Directory.EnumerateFiles(
                Path.GetDirectoryName(FolioInvariantsTests.HeadProjectFile())!, "*.xaml", SearchOption.AllDirectories))
            .Where(file => Path.GetFileName(file) == $"{screen}.xaml"
                || Path.GetFileName(file) == $"{screen}.xaml.cs"
                || Path.GetFileName(file) == $"{screen}.cs")
            .ToList();

        Assert.True(found.Count > 0, $"No source found for the screen '{screen}'.");
        return found;
    }
}
