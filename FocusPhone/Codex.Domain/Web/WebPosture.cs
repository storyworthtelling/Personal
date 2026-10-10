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
/// <item>A trailer is <b>played</b>: script runs (the YouTube IFrame player is script), but
/// nothing navigates at all — not in place, not out to the restricted browser — and nothing
/// plays until the owner taps play inside the embed. Pulp is its one user, and it loads only a
/// YouTube embed of a validated video id (<see cref="PlayerEmbed"/>).</item>
/// </list>
///
/// <para>The members added for the player (<see cref="PlaysMediaWithoutGesture"/>,
/// <see cref="RefusedLinksLeave"/>, <see cref="AllowsFullscreen"/>) default to what the first two
/// postures always did, so adding the third changed neither.</para>
/// </summary>
public sealed record WebPosture(
    bool AllowsScript,
    bool AllowsInPlaceNavigation,
    bool LoadsRemoteImages,
    bool AllowsFileAccess,
    bool SafeBrowsing)
{
    /// <summary>
    /// Whether media may start without a tap. False in every posture: nothing in the suite
    /// autoplays (D77), and WebView's own default is the same.
    /// </summary>
    public bool PlaysMediaWithoutGesture { get; init; }

    /// <summary>
    /// When a navigation is refused in place, whether it is handed out (to the restricted
    /// browser) rather than dropped. Mail hands every link out; the player drops them.
    /// </summary>
    public bool RefusedLinksLeave { get; init; } = true;

    /// <summary>Whether the page may take the screen through the engine's custom-view fullscreen.</summary>
    public bool AllowsFullscreen { get; init; }

    /// <summary>What happens to a navigation the page (or a tap in it) asks for after the initial load.</summary>
    public NavigationVerdict OnNavigation() =>
        AllowsInPlaceNavigation ? NavigationVerdict.Follow
        : RefusedLinksLeave ? NavigationVerdict.HandOut
        : NavigationVerdict.Refuse;

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

    /// <summary>
    /// A YouTube trailer. Script on, because the IFrame player is script; no navigation of any
    /// kind, because a player that follows a link is a browser and the "watch on YouTube" link
    /// would take the owner out of Pulp into the site; fullscreen through the engine's custom
    /// view; and never a frame of video before the owner asks for it.
    /// </summary>
    public static WebPosture Player { get; } = new(
        AllowsScript: true,
        AllowsInPlaceNavigation: false,
        LoadsRemoteImages: true,
        AllowsFileAccess: false,
        SafeBrowsing: true)
    {
        PlaysMediaWithoutGesture = false,
        RefusedLinksLeave = false,
        AllowsFullscreen = true,
    };
}

/// <summary>What a posture does with a navigation request after the initial load.</summary>
public enum NavigationVerdict
{
    /// <summary>Navigate in place (browsing, under the browser's own origin containment).</summary>
    Follow,

    /// <summary>Do not navigate here; hand the URL out to the restricted browser (mail).</summary>
    HandOut,

    /// <summary>Do not navigate anywhere (the player).</summary>
    Refuse,
}

/// <summary>
/// The only thing the <see cref="WebPosture.Player"/> posture loads: a YouTube embed of one video
/// id, at a fixed https origin. A video id arrives from a catalogue response, which is input, so it
/// is checked character by character before it is allowed anywhere near a URL.
///
/// <para>A YouTube video id is eleven characters of the URL-safe base64 alphabet
/// (<c>A–Z a–z 0–9 - _</c>). It encodes 64 bits, so the last character in practice comes from a
/// sixteen-character subset; that tighter rule is not enforced here, because refusing a real
/// trailer over an unconfirmed detail costs more than it protects — the alphabet and length are
/// what keep the id from becoming a path, a query or a host.</para>
/// </summary>
public static class PlayerEmbed
{
    /// <summary>The length of every YouTube video id.</summary>
    public const int VideoIdLength = 11;

    /// <summary>The embed host: YouTube's no-cookie domain, which sets no tracking cookie until play.</summary>
    public const string EmbedHost = "www.youtube-nocookie.com";

    private static readonly string[] EmbedHosts = [EmbedHost, "www.youtube.com", "youtube.com"];

    private const string EmbedPathPrefix = "/embed/";

    /// <summary>True only for exactly eleven characters of the URL-safe base64 alphabet.</summary>
    public static bool IsVideoId(string? candidate)
    {
        if (candidate is null || candidate.Length != VideoIdLength)
        {
            return false;
        }

        foreach (var c in candidate)
        {
            var allowed = c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';
            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The embed URL for a video id, with autoplay explicitly off (D77) and inline playback on.
    /// Throws for anything that is not a video id, so an invalid one can never become a URL.
    /// </summary>
    public static Uri EmbedUrl(string videoId)
    {
        if (!IsVideoId(videoId))
        {
            throw new ArgumentException(
                $"\"{videoId}\" is not a YouTube video id (eleven characters of A-Z, a-z, 0-9, '-' or '_').",
                nameof(videoId));
        }

        return new Uri($"https://{EmbedHost}{EmbedPathPrefix}{videoId}?autoplay=0&playsinline=1");
    }

    /// <summary>
    /// True for an https YouTube embed of one valid video id on the default port with no user
    /// info — the only kind of URL the player posture may hold.
    /// </summary>
    public static bool IsEmbedUrl(Uri url)
    {
        if (!url.IsAbsoluteUri
            || url.Scheme != Uri.UriSchemeHttps
            || !url.IsDefaultPort
            || url.UserInfo.Length > 0
            || !EmbedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url.AbsolutePath;
        return path.StartsWith(EmbedPathPrefix, StringComparison.Ordinal)
            && IsVideoId(path[EmbedPathPrefix.Length..]);
    }

    /// <summary>
    /// The fixed https origin the player page is loaded at. Since 2025 YouTube refuses an embed
    /// whose request carries no Referer (error 153); a page loaded with no base URL has none, so the
    /// page is given one naming the app. Only a lower-case package name is accepted, so the base can
    /// never be steered to a real host's path or user info.
    /// </summary>
    public static string BaseUrlFor(string applicationId)
    {
        var segments = applicationId.Split('.');
        var valid = segments.Length >= 2 && segments.All(segment =>
            segment.Length > 0
            && segment[0] is >= 'a' and <= 'z'
            && segment.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9')));

        if (!valid)
        {
            throw new ArgumentException(
                $"\"{applicationId}\" is not a lower-case package name, so it cannot name the player's origin.",
                nameof(applicationId));
        }

        return $"https://{applicationId}/";
    }
}
