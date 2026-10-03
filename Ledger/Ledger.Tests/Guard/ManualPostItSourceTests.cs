using System.Text.RegularExpressions;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of Phase 2.2's guards (R-G15, R-G16 and the post-it/Add form wiring). The Android
/// head cannot be loaded by this desktop-TFM project, so what has to be true of its wiring is read out of
/// the files themselves — the same content-reading pattern <c>HouseholdGateSourceTests</c> uses. The
/// behavioural halves live in <c>AccountServiceManualEditTests</c>, <c>FigureParserTests</c> and
/// <c>ManualAccountEditingTests</c>.
/// </summary>
public class ManualPostItSourceTests
{
    private const string MainPagePath = "Ledger.Android/Pages/MainPage.xaml.cs";
    private const string AddPagePath = "Ledger.Android/Pages/AddManualAccountPage.xaml.cs";
    private const string BinderPagePath = "Ledger.Android/Pages/LedgerBinderPage.cs";
    private const string PostItViewPath = "Ledger.Android/Views/PostItView.cs";

    // ---- R-G15: an edit updates the card in place and never re-renders the page ----

    /// <summary>
    /// <c>RenderInstitutions</c> replaces <c>bodyHost.Content</c> with a freshly built tree, which destroys the
    /// field the owner just tapped into and drops the keyboard — so moving Balance → APR → Payment needed
    /// reopening the post-it each time. The post-it's save must refresh only the affected card's figures.
    /// </summary>
    [Fact]
    public void ThePostItsSavePathNeverRebuildsThePage()
    {
        var postIt = MethodBody(Source(MainPagePath), "private View BuildManualPostIt(");

        Assert.DoesNotContain("RenderInstitutions(", postIt, StringComparison.Ordinal);
        Assert.DoesNotContain("bodyHost.Content", postIt, StringComparison.Ordinal);
        Assert.DoesNotContain("ReloadAsync(", postIt, StringComparison.Ordinal);
        // The one read of the institutions is the Waiting page's notices, which touch the dog-ear, not the body.
        Assert.Equal(1, Regex.Matches(postIt, @"GetInstitutionsAsync\(").Count);
        Assert.Contains("RefreshNotices(await accounts.GetInstitutionsAsync(", postIt, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePostItSavesOnePatchAtATimeAndNeverAFiveFieldWrite()
    {
        var main = Source(MainPagePath);
        var postIt = MethodBody(main, "private View BuildManualPostIt(");

        Assert.Contains("UpdateManualAccountAsync(", postIt, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateManualAccountFieldsAsync", main, StringComparison.Ordinal);
        // The defaults that used to be written into untouched fields: today as the due date, Month as Every.
        Assert.DoesNotContain("DateTime.Today", postIt, StringComparison.Ordinal);
        Assert.DoesNotContain("new DatePicker", postIt, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePostItsSaveIsGuardedLoggedAndSaysOnePlainSentence()
    {
        var main = Source(MainPagePath);
        var postIt = MethodBody(main, "private View BuildManualPostIt(");

        Assert.DoesNotContain("async void SaveAll", main, StringComparison.Ordinal);
        Assert.Contains("catch (Exception ex)", postIt, StringComparison.Ordinal);
        Assert.Contains("logger.LogError(ex", postIt, StringComparison.Ordinal);
        Assert.Contains("ManualAccountEditing.SaveFailedSentence", postIt, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryRenderedCardCanBeRefreshedInPlace()
    {
        var main = Source(MainPagePath);

        Assert.Contains("cardHosts[account.Id] = cardHost;", main, StringComparison.Ordinal);
        Assert.Contains("cardHosts.Clear();", main, StringComparison.Ordinal);
        Assert.Contains("host.Content = BuildAccountCard(institution, current);", main, StringComparison.Ordinal);
    }

    // ---- R-G16: one parser, on both pages ----

    [Theory]
    [InlineData(MainPagePath)]
    [InlineData(AddPagePath)]
    public void NeitherPageParsesAFigureItself(string path)
    {
        var source = Source(path);

        Assert.DoesNotContain("decimal.TryParse", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryParseDecimal", source, StringComparison.Ordinal);
        Assert.Contains("FigureParser.", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAddFormNeverSavesAnUnreadableFigureAsNoFigure()
    {
        var add = Source(AddPagePath);

        // The old shape: `decimal? apr = TryParse(...) ? value : null;` — an unreadable APR became "no APR".
        Assert.DoesNotMatch(new Regex(@"=\s*TryParse\w*\([^;]*\)\s*\?[^;]*:\s*null"), add);
        Assert.Contains("FigureStatus.Invalid", add, StringComparison.Ordinal);
        Assert.Contains("BlurThenLive", add, StringComparison.Ordinal);
    }

    // ---- the required tint, the keyboard, the Plaid savings post-it, the frame ----

    [Fact]
    public void TheRequiredTintIsWiredToThePredicateOnThePostItAndToTheAddFormsRequiredFields()
    {
        var postIt = MethodBody(Source(MainPagePath), "private View BuildManualPostIt(");
        Assert.Contains("ManualAccountEditing.RequiredTintApplies(", postIt, StringComparison.Ordinal);
        Assert.Contains("apr.Box.SetRequiredTint(needed);", postIt, StringComparison.Ordinal);
        Assert.Contains("payment.Box.SetRequiredTint(needed);", postIt, StringComparison.Ordinal);

        var add = Source(AddPagePath);
        Assert.Contains("nameBox.SetRequiredTint(true);", add, StringComparison.Ordinal);
        Assert.Contains("balanceBox.SetRequiredTint(true);", add, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeyboardHidesThePinnedBarAndPutsItBack()
    {
        var binder = Source(BinderPagePath);

        Assert.Contains("SetSoftInputMode(global::Android.Views.SoftInput.AdjustResize)", binder, StringComparison.Ordinal);
        Assert.Contains("WindowInsets.Type.Ime()", binder, StringComparison.Ordinal);
        Assert.Contains("barSlot.IsVisible = !open", binder, StringComparison.Ordinal);
        // Put back: the window's own mode, the listener, and the bar itself.
        Assert.Contains("window.SetSoftInputMode(before)", binder, StringComparison.Ordinal);
        Assert.Contains("SetOnApplyWindowInsetsListener(null)", binder, StringComparison.Ordinal);
        Assert.Contains("barSlot.IsVisible = true", binder, StringComparison.Ordinal);

        Assert.Matches(new Regex(@"HidesBarForKeyboard\s*=>\s*true"), Source(MainPagePath));
        Assert.Matches(new Regex(@"HidesBarForKeyboard\s*=>\s*true"), Source(AddPagePath));
    }

    [Fact]
    public void APlaidAccountWithNoAprDrawsNoAprColumn()
    {
        var postItView = Source(PostItViewPath);
        Assert.Contains("PostItFacts.For(", postItView, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildFact(\"APR\"", postItView, StringComparison.Ordinal);

        // Null — not a dash — when Plaid reports none.
        Assert.Matches(new Regex(@"var aprText = account\.Apr is \{ \} apr \? [^;]*: null;"), Source(MainPagePath));
    }

    /// <summary>
    /// Phase 2.2 finding 7. <c>LedgerBinderPage.Wrap</c> puts the frame around whatever <c>Content</c> is at
    /// that moment, so a page that sets <c>Content</c> afterwards replaces the frame with the bare form.
    /// <c>AddManualAccountPage</c> did: the page lost its header rule and the binder strip.
    /// </summary>
    [Fact]
    public void TheAddFormBuildsItsBodyBeforeItWrapsIt()
    {
        var add = Source(AddPagePath);

        var build = add.IndexOf("BuildBody();", StringComparison.Ordinal);
        var wrap = add.IndexOf("Wrap(LedgerArea.", StringComparison.Ordinal);
        Assert.True(build >= 0 && wrap >= 0, "The Add form no longer builds its body and wraps it.");
        Assert.True(build < wrap, "The Add form calls Wrap before BuildBody, so BuildBody's Content = … replaces the binder frame.");

        // And nothing else sets Content on the page after the frame is up.
        var contentAssignments = Regex.Matches(add, @"(?<![\w.])Content = ");
        var afterWrap = contentAssignments.Count(m => m.Index > wrap && !IsInsideBuildBody(add, m.Index));
        Assert.Equal(0, afterWrap);
    }

    // ---- helpers ----

    private static bool IsInsideBuildBody(string source, int index)
    {
        var start = source.IndexOf("private void BuildBody()", StringComparison.Ordinal);
        var end = start + MethodBody(source, "private void BuildBody()").Length;
        return index >= start && index <= end;
    }

    /// <summary>The text of a method from its signature to its matching closing brace.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find `{signature}`.");

        var open = source.IndexOf('{', source.IndexOf(')', start));
        var depth = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}' && --depth == 0)
            {
                return source[start..(i + 1)];
            }
        }

        throw new InvalidOperationException($"Unbalanced braces after `{signature}`.");
    }

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
