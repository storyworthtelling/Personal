using System.Text.RegularExpressions;
using Index.Domain.Design;
using Xunit;

namespace Index.Tests.Design;

/// <summary>
/// The Android back press while an action card's haze is up. The card owns its own dismissal and the haze
/// covers the whole page, so back must take the card away and stay on the page, not leave the screen with the
/// card still open behind it. The decision is pure; the page's override is read out of the file, since the
/// Android head cannot be loaded by this desktop-TFM project.
/// </summary>
public class OverlayBackPressTests
{
    [Fact]
    public void WhileTheOverlayIsShowingBackDismissesItAndIsConsumed()
    {
        var action = OverlayBackPress.Decide(overlayShowing: true);

        Assert.Equal(BackPressAction.DismissOverlay, action);
        Assert.True(OverlayBackPress.Consumes(action));
    }

    [Fact]
    public void WithNoOverlayBackIsLeftToThePage()
    {
        var action = OverlayBackPress.Decide(overlayShowing: false);

        Assert.Equal(BackPressAction.Default, action);
        Assert.False(OverlayBackPress.Consumes(action));
    }

    [Fact]
    public void TheBinderPageAsksTheDecisionBeforeAnythingElseAndHidesTheOverlay()
    {
        var source = File.ReadAllText(Path.Combine(DesignTestHelpers.LedgerRoot(), "Ledger.Android/Pages/LedgerBinderPage.cs"));
        var start = source.IndexOf("protected override bool OnBackButtonPressed()", StringComparison.Ordinal);
        Assert.True(start >= 0, "LedgerBinderPage no longer overrides OnBackButtonPressed.");
        var body = source[start..source.IndexOf("private async Task PopAfterCommitAsync", start, StringComparison.Ordinal)];

        var decision = body.IndexOf("OverlayBackPress.Decide(", StringComparison.Ordinal);
        Assert.True(decision >= 0, "OnBackButtonPressed must ask OverlayBackPress whether the overlay is up.");
        Assert.True(decision < body.IndexOf("CommitsBeforeLeaving", StringComparison.Ordinal),
            "The overlay decision must come before the commit-and-pop path.");
        Assert.Matches(new Regex(@"HideOverlay\(\);"), body);
    }
}
