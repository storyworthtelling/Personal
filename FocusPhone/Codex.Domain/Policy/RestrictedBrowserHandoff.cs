namespace Codex.Domain.Policy;

/// <summary>
/// The cross-app handoff for opening an ordinary link in Flyleaf's
/// RestrictedBrowserActivity explicitly, rather than relying solely on the
/// device-owner persistent-preferred-activity pin
/// (PolicyManager.PinRestrictedBrowser). That pin is a system-level default
/// and can be outranked on some hosts by Chrome's own verified Android App
/// Links, which is how a tapped link was observed falling through to full
/// Chrome instead of staying contained. Naming the component explicitly
/// removes the ambiguity for every app that hands a link off to the browser.
/// </summary>
public static class RestrictedBrowserHandoff
{
    public const string Package = PolicyContext.LauncherPackage;
    public const string Activity = "Flyleaf.RestrictedBrowserActivity";
}
