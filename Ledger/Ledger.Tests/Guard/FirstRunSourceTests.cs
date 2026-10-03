using Index.Tests.Design;
using Xunit;

namespace Index.Tests.Guard;

/// <summary>
/// Source-level halves of task 2.6 (mockup 14, first run), R-G22 extending G26. The Android head cannot
/// be loaded by this desktop-TFM project, so what has to be true of its wiring is read out of the files
/// themselves, as <see cref="NoDoubleLinkTests"/> and <see cref="HouseholdGateSourceTests"/> do. The
/// decisions themselves are pure Domain functions tested in <c>FirstRunTests</c>.
/// </summary>
public class FirstRunSourceTests
{
    // ---- finding 1: nothing restored after the passphrase ----
    // (Task 2.7 removed the Save button: the flow these guard now runs from CommitSettingsAsync, when a
    // Settings field is left. Only the method they locate changed — OnSaveClicked became that — not
    // what they require of it.)

    [Fact]
    public void SavingThePassphraseBeginsARestoreWithTheValueItReplaced()
    {
        var save = NoDoubleLinkTests.ExtractMethod(
            Source("Ledger.Android/Pages/SetupPage.xaml.cs"), "private async Task CommitSettingsAsync()");

        Assert.Contains("restoreFlow.BeginAsync(", save, StringComparison.Ordinal);

        // The old value has to be read BEFORE the new one is stored, or "changed" can never be told.
        var remembered = save.IndexOf("SettingsStore.VaultPassphrase;", StringComparison.Ordinal);
        var stored = save.IndexOf("SettingsStore.VaultPassphrase = ", StringComparison.Ordinal);
        Assert.True(remembered >= 0 && stored >= 0 && remembered < stored,
            "CommitSettingsAsync must capture the previous passphrase before it stores the new one.");
    }

    [Fact]
    public void SavePublishesTheVaultOnlyAfterTheRestoreHasFinished()
    {
        var setup = Source("Ledger.Android/Pages/SetupPage.xaml.cs");
        var save = NoDoubleLinkTests.ExtractMethod(setup, "private async Task CommitSettingsAsync()");

        // The restore is begun first, and the publish is handed what it started.
        var begin = save.IndexOf("restoreFlow.BeginAsync(", StringComparison.Ordinal);
        var publish = save.IndexOf("PublishToVaultAsync(run)", StringComparison.Ordinal);
        Assert.True(begin >= 0 && publish > begin, "Save publishes before it has started the restore.");

        // PlaidVaultMerge writes the local bank list over the vault's whenever Plaid keys are present, so
        // publishing before the restore has brought the banks back would erase them from the only copy
        // that survives a factory reset: a restore still in flight must be waited for.
        var publishing = NoDoubleLinkTests.ExtractMethod(setup, "private async Task PublishToVaultAsync(RestoreRun run)");
        Assert.Contains("run.Completion.IsCompleted", publishing, StringComparison.Ordinal);
        Assert.Contains("PublishAfterRestoreAsync(run.Completion)", publishing, StringComparison.Ordinal);

        var deferred = NoDoubleLinkTests.ExtractMethod(setup, "private async Task PublishAfterRestoreAsync(Task restore)");
        var waited = deferred.IndexOf("await restore;", StringComparison.Ordinal);
        var published = deferred.IndexOf("vaultSync.PublishAsync(", StringComparison.Ordinal);
        Assert.True(waited >= 0 && published > waited, "The deferred backup publishes before the restore has finished.");
    }

    [Fact]
    public void TheLaunchRestoreAndThePassphraseRestoreShareOneGate()
    {
        var launch = Source("Ledger.Android/Platforms/Android/MainActivity.cs");
        Assert.Contains("RestoreGate", launch, StringComparison.Ordinal);
        Assert.Contains(".TryStart(", launch, StringComparison.Ordinal);

        var program = Source("Ledger.Android/MauiProgram.cs");
        Assert.Contains("AddSingleton<RestoreGate>", program, StringComparison.Ordinal);
        Assert.Contains("PassphraseRestoreFlow", program, StringComparison.Ordinal);
        Assert.Contains("IOwnerWorkScheduler", program, StringComparison.Ordinal);
    }

    // ---- finding 2: state 3 fills in place; a bank never vanishes ----

    [Fact]
    public void AccountsAsksTheGateAndNeverOffersAddWhileBanksAreComingBack()
    {
        var first = NoDoubleLinkTests.ExtractMethod(
            Source("Ledger.Android/Pages/MainPage.xaml.cs"), "private async Task RenderFirstRunAsync()");

        Assert.Contains("FirstRunPlan.Decide(", first, StringComparison.Ordinal);
        Assert.Contains("restoreGate.IsRunning", first, StringComparison.Ordinal);
        Assert.Contains("FirstRunPlan.OffersAdd(", first, StringComparison.Ordinal);

        var reload = NoDoubleLinkTests.ExtractMethod(
            Source("Ledger.Android/Pages/MainPage.xaml.cs"), "private async Task ReloadAsync()");
        Assert.Contains("RestoreProgress.OffersAdd(", reload, StringComparison.Ordinal);
    }

    [Fact]
    public void ABankWithNoFiguresYetKeepsItsPlaceInsteadOfVanishing()
    {
        var render = NoDoubleLinkTests.ExtractMethod(
            Source("Ledger.Android/Pages/MainPage.xaml.cs"), "private void RenderInstitutions(IReadOnlyList<Institution> institutions)");

        Assert.Contains("RestoreProgress.AwaitsFirstFigures(", render, StringComparison.Ordinal);
        Assert.Contains("RestoreProgress.PendingLine(", render, StringComparison.Ordinal);
        Assert.Contains("RestoreProgress.Of(", render, StringComparison.Ordinal);
        // The "Nothing connected yet" fallback is reached only when nothing at all was drawn, and a
        // pending bank counts as drawn.
        Assert.Contains("anyShown", render, StringComparison.Ordinal);
        Assert.DoesNotContain("anyOpen", render, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePageRefreshesItselfWhileBanksSettleAndStopsWhenItDisappears()
    {
        var main = Source("Ledger.Android/Pages/MainPage.xaml.cs");

        Assert.Contains("Dispatcher.CreateTimer()", main, StringComparison.Ordinal);
        Assert.Contains("MaxSettlePollMinutes", main, StringComparison.Ordinal);
        Assert.Contains("RestoreProgress.Signature(", main, StringComparison.Ordinal);

        var disappearing = NoDoubleLinkTests.ExtractMethod(main, "protected override void OnDisappearing()");
        Assert.Contains("StopSettlePolling()", disappearing, StringComparison.Ordinal);

        var stop = NoDoubleLinkTests.ExtractMethod(main, "private void StopSettlePolling()");
        Assert.Contains(".Stop()", stop, StringComparison.Ordinal);
        Assert.Contains("Tick -=", stop, StringComparison.Ordinal);
    }

    // ---- finding 3: the voiced line stays hidden in first run ----

    [Fact]
    public void TheVoicedLineIsSuppressedInEveryFirstRunState()
    {
        var main = Source("Ledger.Android/Pages/MainPage.xaml.cs");
        // Remediation 3.7: the fixed welcome became the Accounts voice seat; the first-run rule is unchanged.
        var welcome = NoDoubleLinkTests.ExtractMethod(
            main, "private async Task ShowVoiceAsync(bool isHers, HouseholdSnowballHistory? hersHistory)");

        // Checked before the (slow) compose, and again after it, because the first run can begin while
        // the compose is awaiting Drive: this is the call that re-showed the line after it was hidden.
        var early = welcome.IndexOf("if (firstRunShowing)", StringComparison.Ordinal);
        var compose = welcome.IndexOf("voiceSeats.ComposeAsync", StringComparison.Ordinal);
        Assert.True(early >= 0 && early < compose, "ShowVoiceAsync composes before it asks whether this is first run.");
        Assert.Contains("!firstRunShowing", welcome[compose..], StringComparison.Ordinal);

        var reload = NoDoubleLinkTests.ExtractMethod(main, "private async Task ReloadAsync()");
        Assert.Contains("firstRunShowing =", reload, StringComparison.Ordinal);
    }

    // ---- finding 4: "Enter the passphrase" brings the field into view ----

    [Fact]
    public void EnterThePassphraseOpensSettingsWithTheFieldFocused()
    {
        var click = NoDoubleLinkTests.ExtractMethod(
            Source("Ledger.Android/Pages/MainPage.xaml.cs"), "private async void OnPrimaryBarButtonClicked(object? sender, EventArgs e)");
        var begin = click.IndexOf("BeginEnterPassphrase()", StringComparison.Ordinal);
        var push = click.IndexOf("PushAsync(", StringComparison.Ordinal);
        Assert.True(begin >= 0 && begin < push, "The intent must be set on the page before it is pushed.");

        var setup = Source("Ledger.Android/Pages/SetupPage.xaml.cs");
        Assert.Contains("public void BeginEnterPassphrase()", setup, StringComparison.Ordinal);

        var appearing = NoDoubleLinkTests.ExtractMethod(setup, "protected override async void OnAppearing()");
        Assert.Contains("ScrollToAsync(VaultPassphraseEntry", appearing, StringComparison.Ordinal);
        Assert.Contains("VaultPassphraseEntry.Focus()", appearing, StringComparison.Ordinal);
        // Consumed once: coming back to Settings later must not grab the keyboard again.
        Assert.Contains("focusPassphraseOnAppearing = false", appearing, StringComparison.Ordinal);
    }

    // ---- finding 5: the known limit is stated where the proxy lives ----

    [Fact]
    public void TheFileExistsProxyIsMarkedAsAProxyRightAboveIt()
    {
        var lines = Source("Ledger.Android/Pages/MainPage.xaml.cs").Split('\n');
        var index = Array.FindIndex(lines, l => l.Contains("private async Task<bool> VaultHoldsSomethingAsync()", StringComparison.Ordinal));
        Assert.True(index > 0, "VaultHoldsSomethingAsync not found.");

        var above = lines[index - 1].Trim();
        Assert.StartsWith("//", above, StringComparison.Ordinal);
        Assert.Contains("proxy", above, StringComparison.OrdinalIgnoreCase);
    }

    private static string Source(string ledgerRelativePath)
    {
        var path = Path.Combine(DesignTestHelpers.LedgerRoot(), ledgerRelativePath);
        Assert.True(File.Exists(path), $"Expected file not found: {ledgerRelativePath}");
        return File.ReadAllText(path);
    }
}
