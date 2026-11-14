using Folio.Domain.Voice;

namespace Folio.Tests.Voice;

/// <summary>
/// The empty desk's line in the owner's voice is written once a day and then stays put.
///
/// <para>The bug this guards against: the desk composed the line on every refresh — the page
/// refreshes every five seconds — so the line changed under the owner's eyes and every change was a
/// billed model call. Nothing failed; the voice just never held still.</para>
/// </summary>
public class EmptyDeskLineTests
{
    private static readonly DateOnly Today = new(2026, 11, 6);
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoLineYetNeedsOne()
    {
        Assert.True(EmptyDeskLinePlan.NeedsComposing(null, null, null, Now));
        Assert.True(EmptyDeskLinePlan.NeedsComposing("  ", Today, null, Now));
    }

    [Fact]
    public void ALineWrittenTodayStays()
    {
        Assert.False(EmptyDeskLinePlan.NeedsComposing("The desk is clear.", Today, null, Now));
    }

    [Fact]
    public void ALineFromAnEarlierDayIsRewritten()
    {
        Assert.True(EmptyDeskLinePlan.NeedsComposing("The desk is clear.", Today.AddDays(-1), null, Now));
    }

    /// <summary>A line with no recorded day cannot be known to be today's, so it is written once.</summary>
    [Fact]
    public void ALineWithNoRecordedDayIsRewritten()
    {
        Assert.True(EmptyDeskLinePlan.NeedsComposing("The desk is clear.", null, null, Now));
    }

    // Efficiency audit §6: with no voice yet, nothing was stored, so every five-second refresh asked again (a Drive read,
    // and a model call once a profile existed). An attempt that came back empty now waits an hour.
    [Fact]
    public void AnAttemptThatCameBackEmptyWaitsAnHour()
    {
        Assert.False(EmptyDeskLinePlan.NeedsComposing(null, null, Now.AddMinutes(-20), Now));
        Assert.True(EmptyDeskLinePlan.NeedsComposing(null, null, Now.AddMinutes(-61), Now));
    }

    [Fact]
    public void TheDeskRemembersWhenItLastAsked()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
            root = Path.GetDirectoryName(root);
        var page = File.ReadAllText(Path.Combine(root!, "Storyboarder", "Folio.Android", "Pages", "MainPage.xaml.cs"));
        Assert.Contains("SettingsStore.EmptyDeskLineAskedAt", page, StringComparison.Ordinal);
        Assert.Contains("FolioSaves.Count", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The desk must consult the rule and remember what it wrote, rather than composing on every
    /// refresh tick. Read by content because the page lives in the Android head.
    /// </summary>
    [Fact]
    public void TheDeskComposesOnlyWhenTheRuleSaysSo()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);

        var page = File.ReadAllText(Path.Combine(root, "Storyboarder", "Folio.Android", "Pages", "MainPage.xaml.cs"));
        Assert.Contains("EmptyDeskLinePlan.NeedsComposing(", page, StringComparison.Ordinal);
        Assert.Contains("SettingsStore.EmptyDeskLineDay", page, StringComparison.Ordinal);
    }
}
