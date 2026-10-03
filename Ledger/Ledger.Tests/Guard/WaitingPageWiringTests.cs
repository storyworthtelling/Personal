using System.Text.RegularExpressions;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// R6 / remediation 3.5, the parts of the Waiting page that live in Index's own screens. The pages are
/// MAUI views and cannot be constructed in this desktop run, so these read the source.
///
/// <para>Finding 4: the actions used to find their target by matching the notice's HEADLINE TEXT
/// ("{institution.Name} needs reconnecting" began with the name; the account was found by
/// <c>Headline.Contains(account.Name)</c>). The new headlines name the institution, not the account,
/// so that lookup silently stops working — a tap on "Open Evergreen Home Lending" would land on nothing. The action
/// now carries the id it acts on.</para>
/// </summary>
public class WaitingPageWiringTests
{
    [Fact]
    public void NoActionFindsItsTargetByMatchingHeadlineText()
    {
        var source = MainPage();

        Assert.DoesNotContain("Headline.StartsWith", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Headline.Contains", source, StringComparison.Ordinal);
        Assert.DoesNotContain("notice.Headline", MethodBody(source, "OpenReconnectFromNoticeAsync"), StringComparison.Ordinal);
        Assert.DoesNotContain("notice.Headline", MethodBody(source, "OpenAccountFromNoticeAsync"), StringComparison.Ordinal);
    }

    [Fact]
    public void NoActionIsChosenByComparingTheActionLabelString()
    {
        // "Reconnect" / "Open it" were the labels; the new ones name the bank, so a string switch on
        // them cannot work and must not return.
        Assert.DoesNotContain("\"Reconnect\" =>", MainPage(), StringComparison.Ordinal);
        Assert.DoesNotContain("\"Open it\" =>", MainPage(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheNoticesCarryTheirDateIntoTheDogEar()
    {
        var toDogEar = MethodBody(MainPage(), "ToDogEarNotice");

        Assert.Contains("At:", toDogEar, StringComparison.Ordinal);
        Assert.Contains("notice.At", toDogEar, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MainPage.xaml.cs")]
    [InlineData("BudgetPage.xaml.cs")]
    public void TheTurnedPageIsIndexsOwnFramePage_NotTheBareKitPage(string page)
    {
        var source = Read(page);

        Assert.DoesNotContain("new WaitingPage(", source, StringComparison.Ordinal);
        Assert.Contains("new LedgerWaitingPage(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheEarSitsInTheBinderPagesCornerNotInsideTheWell()
    {
        var binder = Read("LedgerBinderPage.cs");
        Assert.Contains("SetCorner(", binder, StringComparison.Ordinal);

        foreach (var page in new[] { "MainPage.xaml.cs", "BudgetPage.xaml.cs", "SavingsPage.xaml.cs", "SetupPage.xaml.cs" })
        {
            var source = Read(page);
            Assert.Contains("SetCorner(dogEar)", source, StringComparison.Ordinal);
            Assert.DoesNotMatch(new Regex(@"(rootLayout|root|overlay)\.Children\.Add\(dogEar\)"), source);
        }
    }

    [Fact]
    public void TheWaitingFrameLightsItsAreaAndSaysWhatItIsFor()
    {
        var source = Read("LedgerWaitingPage.cs");

        Assert.Contains(": LedgerBinderPage", source, StringComparison.Ordinal);
        Assert.Contains("Wrap(area, \"Waiting\")", source, StringComparison.Ordinal);
        Assert.Contains("newest first", source, StringComparison.Ordinal);
        // The graph paper is the frame's since Phase 4, so it runs up behind the header (mockup 17).
        Assert.Contains("PaperBehindHeader => true", source, StringComparison.Ordinal);
        Assert.Contains("WaitingView", source, StringComparison.Ordinal);
    }

    private static string MainPage() => Read("MainPage.xaml.cs");

    private static string Read(string page)
    {
        var path = Path.Combine(RepoRoot(), "Ledger", "Ledger.Android", "Pages", page);
        Assert.True(File.Exists(path), $"Expected file not found: {page}");
        return File.ReadAllText(path);
    }

    private static string MethodBody(string source, string name)
    {
        var declaration = Regex.Match(
            source, @"^[ \t]*(?:(?:public|private|protected|internal|static|async|override)\s+)+[\w<>\[\]?,\s]+?\b"
                + Regex.Escape(name) + @"\s*\(", RegexOptions.Multiline);
        Assert.True(declaration.Success, $"No method named {name}.");

        var open = source.IndexOf('{', declaration.Index);
        var arrow = source.IndexOf("=>", declaration.Index, StringComparison.Ordinal);
        if (arrow >= 0 && (open < 0 || arrow < open))
        {
            return source[arrow..source.IndexOf(';', arrow)];
        }

        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0) return source[open..(i + 1)];
        }

        throw new InvalidOperationException($"Unbalanced braces in {name}.");
    }

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
