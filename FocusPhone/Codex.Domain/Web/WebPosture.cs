namespace Codex.Domain.Web;

/// <summary>
/// How a web view is allowed to behave. Two postures, one engine.
///
/// The suite has exactly one hardened browser configuration, and it is shared rather than
/// copied — a second WebView configured separately is a second place to get wrong, and the
/// one that gets forgotten is the one that gets exploited. What differs between a page the
/// owner chose to visit and an email somebody chose to send them is not the hardening but
/// the <b>posture</b>:
///
/// <list type="bullet">
/// <item>A page is <b>browsed</b>: script runs (half the web will not render otherwise) and
/// a link within the site follows in place under origin containment.</item>
/// <item>An email is <b>read</b>: nothing in it executes, and no link in it navigates the
/// reader. Every link leaves for the restricted browser, where the same containment rules
/// meet it as any other link. Mail is the one surface whose content is written by whoever
/// felt like writing to the owner.</item>
/// </list>
/// </summary>
public sealed record WebPosture(
    bool AllowsScript,
    bool AllowsInPlaceNavigation,
    bool LoadsRemoteImages,
    bool AllowsFileAccess,
    bool SafeBrowsing)
{
    /// <summary>A page the owner chose to visit, under origin containment.</summary>
    public static WebPosture Browsing { get; } = new(
        AllowsScript: true,
        AllowsInPlaceNavigation: true,
        LoadsRemoteImages: true,
        AllowsFileAccess: false,
        SafeBrowsing: true);

    /// <summary>
    /// An email. Remote images load — the owner's explicit decision, accepting the tracking
    /// pixel in exchange for mail that looks like mail.
    /// </summary>
    public static WebPosture Reading { get; } = new(
        AllowsScript: false,
        AllowsInPlaceNavigation: false,
        LoadsRemoteImages: true,
        AllowsFileAccess: false,
        SafeBrowsing: true);
}
