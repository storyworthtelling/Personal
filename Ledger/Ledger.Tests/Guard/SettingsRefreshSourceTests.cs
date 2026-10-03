using System.Text.RegularExpressions;
using System.Xml.Linq;
using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of task 2.7 (mockups 10 and 11). The Android head cannot be loaded by this
/// desktop-TFM project, so what has to be true of its wiring is read out of the files themselves, as
/// <see cref="FirstRunSourceTests"/> and <see cref="HouseholdGateSourceTests"/> do. The decisions are
/// pure Domain functions tested in <c>SettingsSelfSaveTests</c>.
/// </summary>
public class SettingsRefreshSourceTests
{
    private const string SetupCode = "Ledger.Android/Pages/SetupPage.xaml.cs";
    private const string SetupXaml = "Ledger.Android/Pages/SetupPage.xaml";
    private const string MainCode = "Ledger.Android/Pages/MainPage.xaml.cs";

    // ---- finding 1: no pinned bar, fields save themselves, nothing pops ----

    [Fact]
    public void SettingsHasNoPinnedSaveBarAndNeverPopsItself()
    {
        var setup = Source(SetupCode);

        Assert.DoesNotContain("Save settings", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("SetBar(", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("saveSettings", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("PopAsync", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("Save settings", Source(SetupXaml), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ClientIdEntry")]
    [InlineData("SecretEntry")]
    [InlineData("VaultPassphraseEntry")]
    public void EveryFieldCommitsWhenItIsLeftAndWhenEntryIsCompleted(string entry)
    {
        var setup = Source(SetupCode);

        Assert.Contains($"{entry}.Unfocused +=", setup, StringComparison.Ordinal);
        Assert.Contains($"{entry}.Completed +=", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void LeavingThePageCommitsWhateverIsStillBeingTyped()
    {
        var disappearing = NoDoubleLinkTests.ExtractMethod(Source(SetupCode), "protected override void OnDisappearing()");

        Assert.Contains("CommitSafelyAsync()", disappearing, StringComparison.Ordinal);
    }

    [Fact]
    public void ATabTurnOrBackPressCommitsBeforeItLeavesSoAccountsNeverOpensOnAHalfStartedRestore()
    {
        // OnDisappearing fires after the next page has started to appear, so a commit begun there can lose
        // the race with Accounts' first paint (R-G22: Add must never show while the banks come back).
        // Leaving through the strip or back waits for the commit first, as Save made the page wait.
        var setup = Source(SetupCode);
        Assert.Contains("protected override bool CommitsBeforeLeaving => true;", setup, StringComparison.Ordinal);
        Assert.Contains("protected override Task BeforeLeavingAsync() => CommitSafelyAsync();", setup, StringComparison.Ordinal);

        var binder = Source("Ledger.Android/Pages/LedgerBinderPage.cs");
        var area = NoDoubleLinkTests.ExtractMethod(binder, "private async void OnAreaSelected(int index)");
        var wait = area.IndexOf("await BeforeLeavingAsync();", StringComparison.Ordinal);
        var pop = area.IndexOf("PopToRootAsync", StringComparison.Ordinal);
        Assert.True(wait >= 0 && wait < pop, "The tab turn must await the commit before it pops.");
        Assert.Contains("protected override bool OnBackButtonPressed()", binder, StringComparison.Ordinal);
    }

    [Fact]
    public void ACommitIsOnlyMadeForAFieldThatChanged()
    {
        var commit = NoDoubleLinkTests.ExtractMethod(Source(SetupCode), CommitDeclaration);

        Assert.Contains("SettingsCommitPlan.Decide(", commit, StringComparison.Ordinal);
        Assert.Contains("if (!plan.Any)", commit, StringComparison.Ordinal);

        // The decision comes before anything is stored, and the early return before the restore flow:
        // an unchanged value must not reach BeginAsync or the vault at all.
        var decide = commit.IndexOf("SettingsCommitPlan.Decide(", StringComparison.Ordinal);
        var gate = commit.IndexOf("if (!plan.Any)", StringComparison.Ordinal);
        var store = commit.IndexOf("SettingsStore.PlaidClientId = ", StringComparison.Ordinal);
        var begin = commit.IndexOf("restoreFlow.BeginAsync(", StringComparison.Ordinal);
        Assert.True(decide >= 0 && decide < gate && gate < store && store < begin,
            "Decide, then return when nothing changed, then store, then the restore flow.");
    }

    [Fact]
    public void ThePlaidFieldsReachTheVaultOnlyThroughTheRoleGateAndTheRestoreWait()
    {
        var commit = NoDoubleLinkTests.ExtractMethod(Source(SetupCode), CommitDeclaration);

        // BeginAsync is called for a Plaid-only change too (its plan restores nothing then): that call is
        // how a publish learns about a restore still in flight and waits for it.
        Assert.Contains("restoreFlow.BeginAsync(", commit, StringComparison.Ordinal);
        Assert.Matches(@"if \(InstallRoleGate\.MayRunPlaidWork\(role\)\)\s*\{\s*await PublishToVaultAsync\(run\);", commit);
    }

    // ---- finding 2: the category undo is the dog-ear, near the thumb ----

    [Fact]
    public void RemovingACategoryFoldsTheDogEarInsteadOfRaisingTheBarAtTheTopOfThePage()
    {
        var setup = Source(SetupCode);
        var tapped = NoDoubleLinkTests.ExtractMethod(setup, "private async void OnCategoryTapped(object? sender, EventArgs e)");
        var remove = tapped[tapped.IndexOf("case \"Remove\":", StringComparison.Ordinal)..];

        Assert.Contains("dogEarState.BeginUndo(", remove, StringComparison.Ordinal);
        Assert.Contains("DesignTokens.Motion.UndoWindowMs", remove, StringComparison.Ordinal);
        Assert.Contains("dogEar.Refresh()", remove, StringComparison.Ordinal);
        Assert.DoesNotContain("UndoBar", remove, StringComparison.Ordinal);
        Assert.DoesNotContain("UndoLabel", remove, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginUndo($", remove, StringComparison.Ordinal);
        Assert.DoesNotContain("pendingCategoryRemoval", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDogEarIsThePagesCornerNotAChildOfTheScrollingContent()
    {
        var setup = Source(SetupCode);

        Assert.Contains("new DogEar(dogEarState)", setup, StringComparison.Ordinal);
        // Remediation 3.5 (mockup 17): the frame holds the corner, flush to the binder gutter at the
        // page's top, instead of a screen overlaying it on its own content. It is handed over once the
        // frame exists.
        Assert.DoesNotMatch(@"Children\.Add\(dogEar\)|\.Add\(dogEar\)", setup);
        Assert.True(
            setup.IndexOf("SetCorner(dogEar)", StringComparison.Ordinal) is var corner and >= 0
            && corner > setup.IndexOf("Wrap(LedgerArea.Settings", StringComparison.Ordinal),
            "The dog-ear has to be given to the frame's corner after Wrap has built it.");
    }

    [Fact]
    public void AnUndoThatIsUnreachableOrUnreadableLeavesNoBespokeBarBehind()
    {
        // The institution removal still has a bar, in its own section next to the Remove button, where the
        // owner is looking. A category's undo may not borrow it.
        var setup = Source(SetupCode);
        var xaml = Source(SetupXaml);

        Assert.DoesNotContain("pendingCategoryRemoval", setup, StringComparison.Ordinal);
        var uses = Regex.Matches(setup, @"BeginUndo\(\$""Removed \{categoryName\}");
        Assert.Empty(uses);
        // And the bar stays inside the Connected accounts section, not at page level.
        var bar = xaml.IndexOf("x:Name=\"UndoBar\"", StringComparison.Ordinal);
        var sectionStart = xaml.IndexOf("x:Name=\"ConnectedAccountsSection\"", StringComparison.Ordinal);
        var sectionEnd = xaml.IndexOf("ConnectedAccountsDivider", StringComparison.Ordinal);
        Assert.True(bar > sectionStart && bar < sectionEnd);
    }

    // ---- finding 5: the design annotation that leaked into the Shared keys row ----

    [Fact]
    public void TheDeveloperNoteIsGoneFromTheSharedKeysRow()
    {
        var xaml = Source(SetupXaml);
        var setup = Source(SetupCode);

        Assert.DoesNotContain("Shown here so a quiet greeting", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("so there's no Replace", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("so there's no Replace", setup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSharedKeysRowReadsWorkingOnTheLeftAndSetInFolioAloneOnTheRight()
    {
        var xaml = Source(SetupXaml);
        var doc = XDocument.Parse(xaml);
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        var section = doc.Descendants().Single(e => (string?)e.Attribute(x + "Name") == "SharedKeysSection");

        var labels = section.Descendants().Where(e => e.Name.LocalName == "Label").ToList();
        var texts = labels.Select(l => (string?)l.Attribute("Text")).Where(t => t is not null).ToList();
        Assert.Contains("Claude, for the voice", texts);
        Assert.Contains("Set in Folio", texts);

        // The status label shares the left cell with the name; "Set in Folio" is the only thing right of it.
        var status = labels.Single(l => (string?)l.Attribute(x + "Name") == "SharedKeyStatusLabel");
        Assert.Equal("0", (string?)status.Attribute("Grid.Column") ?? "0");
        var folio = labels.Single(l => (string?)l.Attribute("Text") == "Set in Folio");
        Assert.Equal("1", (string?)folio.Attribute("Grid.Column"));

        var apply = NoDoubleLinkTests.ExtractMethod(Source(SetupCode), "private async Task ApplySharedKeyStatusAsync()");
        Assert.Contains("\"✓ Working\"", apply, StringComparison.Ordinal);
    }

    // ---- finding 6: her Settings is mockup 11 ----

    [Fact]
    public void SettingsTakesItsRoleDependentCopyFromTheDomainNotFromLiteralsInThePage()
    {
        var setup = Source(SetupCode);
        var xaml = Source(SetupXaml);

        Assert.Contains("SettingsCopy.VaultHeading(", setup, StringComparison.Ordinal);
        Assert.Contains("SettingsCopy.PassphraseNote(", setup, StringComparison.Ordinal);
        Assert.Contains("SettingsCopy.SignedInLine(", setup, StringComparison.Ordinal);
        Assert.Contains("SettingsCopy.ShowsSignIn(", setup, StringComparison.Ordinal);
        // The owner's sentences moved to the Domain so there is one copy of each.
        Assert.DoesNotContain("your Plaid credentials and each connected bank", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Shown in the clear so you can read it back", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void NoPlaidWordingSitsOutsideTheSectionsHerInstallHides()
    {
        // Everything on this page that talks about Plaid or a factory reset must live inside a wrapper
        // ApplyInstallRoleAsync hides for her; anything outside would show on her screen.
        var doc = XDocument.Parse(Source(SetupXaml));
        XNamespace x = "http://schemas.microsoft.com/winfx/2009/xaml";
        var hiddenForHer = new HashSet<string>
        {
            "PlaidSection", "SharedKeysSection", "ConnectedAccountsSection", "BudgetSection", "DiagnosticsSection",
        };

        var leaks = new List<string>();
        foreach (var element in doc.Descendants())
        {
            foreach (var attribute in element.Attributes().Where(a => a.Name.LocalName is "Text" or "Placeholder"))
            {
                if (!Regex.IsMatch(attribute.Value, "Plaid|factory reset|access token", RegexOptions.IgnoreCase))
                {
                    continue;
                }

                var hidden = element.AncestorsAndSelf()
                    .Any(a => a.Attribute(x + "Name")?.Value is { } name && hiddenForHer.Contains(name));
                if (!hidden)
                {
                    leaks.Add(attribute.Value);
                }
            }
        }

        Assert.True(leaks.Count == 0, "Plaid wording outside the sections her install hides: " + string.Join(" | ", leaks));
    }

    [Fact]
    public void MockupElevenSaysWhatTheDomainSays()
    {
        // The strings under test are copied from the mockup; this proves the copy matches the file.
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), "design", "mockup-11.html");
        var html = File.ReadAllText(path);

        Assert.Contains("The same passphrase as on the other phone. It opens the balances Index shares with this one.", html, StringComparison.Ordinal);
        Assert.Contains("&middot; Drive, where the shared balances and the voice are read", html, StringComparison.Ordinal);
        Assert.Contains(">Vault</h3>", html, StringComparison.Ordinal);
        Assert.Contains(">Signed in</div>", html, StringComparison.Ordinal);
    }

    // ---- finding 3: refresh on open ----

    [Fact]
    public void AccountsRefreshesWhenItAppearsThroughTheExistingWorkerAndTheGate()
    {
        var main = Source(MainCode);
        var appearing = NoDoubleLinkTests.ExtractMethod(main, "protected override async void OnAppearing()");

        var hers = appearing.IndexOf("household.IsHers", StringComparison.Ordinal);
        var start = appearing.IndexOf("StartRefreshOnOpen()", StringComparison.Ordinal);
        Assert.True(hers >= 0 && start > hers, "The refresh is started after her branch has returned.");

        var starting = NoDoubleLinkTests.ExtractMethod(main, "private void StartRefreshOnOpen()");
        Assert.Contains("RefreshOnOpen.ShouldStart(", starting, StringComparison.Ordinal);
        Assert.Contains("roles.Current", starting, StringComparison.Ordinal);
        Assert.Contains("ownerWork.IsConfigured", starting, StringComparison.Ordinal);
        // Through task 2.6's seam over RefreshWorker.RunNow (OwnerWork), the one "refresh now" entry point.
        Assert.Contains("ownerWork.RunRefreshNow()", starting, StringComparison.Ordinal);

        // One refresh implementation: the page asks the worker, it does not run the pipeline itself.
        Assert.DoesNotContain("PipelineRunner", main, StringComparison.Ordinal);
        Assert.DoesNotContain("RunUntilExhaustedAsync", main, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCachedBalancesArePaintedBeforeTheRefreshIsStarted()
    {
        var appearing = NoDoubleLinkTests.ExtractMethod(Source(MainCode), "protected override async void OnAppearing()");

        var paint = appearing.IndexOf("await ReloadAsync();", StringComparison.Ordinal);
        var start = appearing.IndexOf("StartRefreshOnOpen()", StringComparison.Ordinal);
        Assert.True(paint >= 0 && start > paint);
    }

    [Fact]
    public void ReturningToTheAppAlsoRefreshes()
    {
        var main = Source(MainCode);

        Assert.Matches(@"Window\.Resumed \+= OnWindowResumed", main);
        Assert.Matches(@"Window\.Resumed -= OnWindowResumed", main);
    }

    [Fact]
    public void TheStatusLineSaysRefreshingWhileItRunsAndThePollWatchesForTheEnd()
    {
        var main = Source(MainCode);

        Assert.Contains("RefreshOnOpen.RefreshingSentence", main, StringComparison.Ordinal);
        Assert.Contains("RefreshWorker.IsCycleRunning", main, StringComparison.Ordinal);
        Assert.Contains("RefreshOnOpen.IsActive(", main, StringComparison.Ordinal);

        // The refresh state is part of what the poll compares, or a refresh that changed no figure would
        // never flip the sentence to "Refreshed just now".
        var signature = NoDoubleLinkTests.ExtractMethod(main, "private string PageSignature(IReadOnlyList<Institution> institutions, bool restoring)");
        Assert.Contains("RefreshActive()", signature, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefreshFinishingNeverRebuildsAPostItThatIsBeingEdited()
    {
        var main = Source(MainCode);
        var tick = NoDoubleLinkTests.ExtractMethod(main, "private async void OnSettleTick(object? sender, EventArgs e)");

        Assert.Contains("postItEditing", tick, StringComparison.Ordinal);
        Assert.Contains("entry.Focused +=", main, StringComparison.Ordinal);
        Assert.Contains("entry.Unfocused +=", main, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWorkerExposesWhetherACycleIsRunningFromItsOwnGate()
    {
        var worker = Source("Ledger.Android/RefreshWorker.cs");

        Assert.Matches(@"public static bool IsCycleRunning\s*=>\s*CycleGate\.CurrentCount == 0;", worker);
    }

    [Fact]
    public void TheLastRunSummaryHasNoEmDash()
    {
        var store = Source("Ledger.Android/SettingsStore.cs");
        var line = store[store.IndexOf("public static string LastRunSummary", StringComparison.Ordinal)..];
        line = line[..line.IndexOf(';', StringComparison.Ordinal)];

        Assert.Contains("RefreshOnOpen.Summary(", line, StringComparison.Ordinal);
        Assert.DoesNotContain("—", line, StringComparison.Ordinal);
    }

    private const string CommitDeclaration = "private async Task CommitSettingsAsync()";

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
