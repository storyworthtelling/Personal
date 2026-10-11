namespace Pulp.Tests.Discover;

/// <summary>
/// The size chosen in Settings (D106) must be the size Discover deals, and only when it deals: the kept
/// week is never touched by a change (D112, D220). Both are properties of the head's wiring, so they are
/// read by content, and the guard asserts it found the screen.
/// </summary>
public class StackSizeReachTests
{
    private static string Discover() =>
        File.ReadAllText(PulpSource.Files(".cs").Single(file => PulpSource.Relative(file) == "Pulp.Android/Shell/DiscoverArea.cs"));

    [Fact]
    public void DiscoverDealsTheSizeSettingsRecorded()
    {
        var text = Discover();

        Assert.Contains("DiscoverPageWeek.Deal(", text, StringComparison.Ordinal);
        Assert.Contains("new StackSizeStore(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("StackSizes.Default", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheKeptWeekIsJudgedByTheWeeksOwnRuleNotByWhatSizeIsNow()
    {
        var text = Discover();
        var load = text[text.IndexOf("private async Task LoadAsync", StringComparison.Ordinal)..];

        Assert.Contains(".Keeps(who, now)", load, StringComparison.Ordinal);

        // The size is read only on the way to dealing, after the kept week has been given its chance.
        Assert.True(load.IndexOf(".Keeps(who, now)", StringComparison.Ordinal) < load.IndexOf("new StackSizeStore(", StringComparison.Ordinal),
            "The size is read before the kept week is judged; a change could reach the week in hand.");
    }
}
