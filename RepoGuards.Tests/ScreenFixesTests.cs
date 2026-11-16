namespace RepoGuards.Tests;

/// <summary>
/// W8.9's screen-level fixes (audit 02 §11, §24; 04 §11–12, §16, §31–32), held so they do not drift back.
/// </summary>
public class ScreenFixesTests
{
    [Fact]
    public void EveryNativeScreenHeaderCarriesItsMarkerRule()
    {
        // §7: a screen opens with the Serif header AND the marker underline. The bare title is gone.
        Assert.DoesNotContain("public static TextView PageHeader(", Read("FocusPhone/Codex.Ui.Android/PaperUi.cs"), StringComparison.Ordinal);
        Assert.Contains("PaperUi.ScreenHeader(", Read("FocusPhone/Flyleaf.Android/UnlockActivity.cs"), StringComparison.Ordinal);
        Assert.Contains("PaperUi.ScreenHeader(", Read("FocusPhone/Codex.Ui.Android/Diagnostics/CrashViewerActivity.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("FocusPhone/Flyleaf.Android/HomeActivity.cs")]
    [InlineData("FocusPhone/Post.Android/MailboxArea.cs")]
    public void AVoicedSeatIsInTheVoiceHand(string file) =>
        // §4, §14: a voiced line is Italianno in ink-blue.
        Assert.Contains("PaperUi.Voice(", Read(file), StringComparison.Ordinal);

    [Fact]
    public void TheFlyleafCoverDrawsTheOwnersMarkNotALetter()
    {
        var cover = Read("FocusPhone/Flyleaf.Android/CoverView.cs");
        Assert.DoesNotContain("DrawText(\"M\"", cover, StringComparison.Ordinal);
        Assert.Contains("owner_medallion", cover, StringComparison.Ordinal);
    }

    [Fact]
    public void PostsSettingsLabelsKeepTheMutedInk() =>
        // §4: the Label role is muted; re-inking it to full Ink made labels shout over their fields.
        Assert.DoesNotContain("labelView.SetTextColor(PaperTheme.Ink)", Read("FocusPhone/Post.Android/SettingsArea.cs"), StringComparison.Ordinal);

    [Fact]
    public void IndexSettingsUndoesThroughTheDogEarAndHasNoKebab()
    {
        var page = Read("Ledger/Ledger.Android/Pages/SetupPage.xaml") + Read("Ledger/Ledger.Android/Pages/SetupPage.xaml.cs");
        Assert.DoesNotContain("UndoBar", page, StringComparison.Ordinal);
        Assert.DoesNotContain("⋮", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSnowballSaysWhereTheExtraGoes()
    {
        // Mockup 6: the caption under "Extra each week". The audit found it missing; it had since landed as
        // PlanText.ExtraHint (its wording held by Ledger.Tests' PlanTextTests), so this holds the page to it.
        var page = Read("Ledger/Ledger.Android/Pages/SnowballPage.xaml.cs");
        Assert.Contains("Text = PlanText.ExtraHint", page, StringComparison.Ordinal);
        Assert.Contains("loansSideRoot.Children.Add(extraHintLabel)", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// A Pulp page behind a role tab takes that tab's hue for its accents (owner's ruling, W8; DESIGN.md §9): the
    /// Discover filter and mode chips, the child Discover page, the Sports team finder and the Playlists filter chip
    /// used to name the amber identity directly.
    /// </summary>
    [Theory]
    [InlineData("Pulp/Pulp.Android/Shell/DiscoverArea.cs")]
    [InlineData("Pulp/Pulp.Android/Shell/ChildDiscoverPage.cs")]
    [InlineData("Pulp/Pulp.Android/Discover/ModeChips.cs")]
    [InlineData("Pulp/Pulp.Android/Sports/SportsScreen.cs")]
    [InlineData("Pulp/Pulp.Android/Playlists/PlaylistsScreen.cs")]
    public void APulpPageWearsItsTabsHue(string file)
    {
        var text = Read(file);
        Assert.DoesNotContain("DesignTokens.Identity.Pulp.Mark", text, StringComparison.Ordinal);
        Assert.Contains("AreaPalette.For(PulpAreas.For(PulpArea.", text, StringComparison.Ordinal);
    }

    private static string Read(string file) => File.ReadAllText(Path.Combine(GuardScan.RepoRoot(), file));
}
