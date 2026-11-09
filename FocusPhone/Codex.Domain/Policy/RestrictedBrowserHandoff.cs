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

    /// <summary>
    /// The activity's registered Java name, which the activity declares as its <c>Name</c>. It was the C#
    /// name, <c>Flyleaf.RestrictedBrowserActivity</c>, which nothing registered: with no declared name the
    /// activity lives under a generated <c>crc64…</c> package, so every explicit hand-off missed it.
    /// </summary>
    public const string Activity = PolicyContext.LauncherPackage + ".RestrictedBrowserActivity";
}
