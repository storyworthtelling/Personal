namespace Folio.Tests.Voice;

/// <summary>
/// The empty desk's line in the owner's voice is written by the suite's one rule (<c>SeatPlan</c>, audit W10.3): once
/// a day, kept across a restart in the seat memory file, and an empty attempt (no voice yet) tried again after ten
/// minutes. The desk refreshes every few seconds, so composing on each refresh rewrote the line under the owner's
/// eyes and billed a model call each time. Read by content because the page lives in the Android head.
/// </summary>
public class EmptyDeskLineTests
{
    private static string Page()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CLAUDE.md")))
            root = Path.GetDirectoryName(root);
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root, "Storyboarder", "Folio.Android", "Pages", "MainPage.xaml.cs"));
    }

    [Fact]
    public void TheDeskComposesOnlyWhenTheOneRuleSaysSo()
    {
        var page = Page();

        Assert.Contains("SeatPlan.Decide(", page, StringComparison.Ordinal);
        Assert.Contains("SeatMemoryFile", page, StringComparison.Ordinal);
        Assert.Contains("SeatPlan.Remember(", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDeskStillRedrawsOnlyWhenSomethingWasSaved()
    {
        Assert.Contains("FolioSaves.Count", Page(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheEmptyDeskIsOnTheCallSheet()
    {
        Assert.Equal("empty-desk", Folio.Domain.Voice.VoicedOccasions.Empty.Seat);
        Assert.Equal("desk-line", Folio.Domain.Voice.VoicedOccasions.Desk.Seat);
    }
}
