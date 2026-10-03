namespace Index.Domain.Design;

/// <summary>What the Android back press does on a binder page.</summary>
public enum BackPressAction
{
    /// <summary>The page's own back behaviour (commit-and-pop, or the default).</summary>
    Default,

    /// <summary>An action card's haze is up: take it away and stay on the page.</summary>
    DismissOverlay,
}

/// <summary>
/// The back press while an action card is open. The haze covers the whole page and the card owns its own
/// dismissal, so a back press that left the screen would strand the card open behind a page the owner has
/// already gone from. Pure, so the decision is tested without the Android head.
/// </summary>
public static class OverlayBackPress
{
    public static BackPressAction Decide(bool overlayShowing) =>
        overlayShowing ? BackPressAction.DismissOverlay : BackPressAction.Default;

    /// <summary>True when the press is spent: the page must report it handled and go nowhere.</summary>
    public static bool Consumes(BackPressAction action) => action == BackPressAction.DismissOverlay;
}
