using System.Text.RegularExpressions;
using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>
/// The wire contract Pulp asks Flyleaf to open a title <i>locked</i> over (P12 Release G, D288). On the
/// locked phone Pulp hands a title to the service's own app; this request asks Flyleaf to start that
/// deep link in lock task mode, so the film is a room with one door (Home) rather than an app the
/// phone can wander out of.
///
/// An <b>explicit</b> broadcast naming this receiver, under a signature-level permission the launcher
/// declares, exactly the pattern of <see cref="HiddenAppsHandoff"/>: naming the component means nothing
/// else can intercept it and the permission means nothing else can send one. Signed is not trusted,
/// though: every suite app shares one keystore, so <see cref="LockedOpenGate"/> decides what a signed
/// sender may ask for (D296). The payload crosses as two exact-text extras. Pulp's
/// <c>LockedOpenContractTests</c> (guard G19) reads this file by content, so neither half can move alone.
/// </summary>
public static class LockedOpenHandoff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.LockedOpenReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.LOCKED_OPEN";

    /// <summary>Declared by Flyleaf at protectionLevel="signature"; Pulp must hold it to send.</summary>
    public const string Permission = "com.storyworthtelling.focusphone.launcher.RECEIVE_LOCKED_OPEN";

    /// <summary>The service app's package id.</summary>
    public const string PackageExtra = "package";

    /// <summary>The title's https deep link in that app.</summary>
    public const string LinkExtra = "link";

    /// <summary>
    /// The answer to the ask (D301). The request is an ordered broadcast and Flyleaf's receiver sets this
    /// as the result code only when a locked session actually started: Activity.RESULT_OK.
    /// </summary>
    public const int ResultStarted = -1;

    /// <summary>The result code for everything else: refused, or could not start. Also the initial code, so silence reads as this.</summary>
    public const int ResultRefused = 0;

    /// <summary>The preferences file the locked-session record persists in; a storage key, kept stable.</summary>
    public const string PreferencesName = "lockedSession";

    /// <summary>A link is a label, not a document.</summary>
    public const int MaxLinkLength = 2048;
}

/// <summary>A request to open <see cref="Link"/> in <see cref="Package"/>, locked.</summary>
public sealed record LockedOpenRequest(string Package, Uri Link);

/// <summary>Why a request was refused, or <see cref="None"/>.</summary>
public enum LockedOpenRefusal
{
    None,

    /// <summary>The payload was missing, garbled, or not an https link to a package-shaped id.</summary>
    Unreadable,

    /// <summary>Under the owner's own namespace: a signed sender may not lock-task Post, Plate or the launcher itself.</summary>
    OwnerNamespace,

    /// <summary>Not one of the streaming services the catalogue names.</summary>
    NotStreamingService,

    /// <summary>A catalogued service the household did not tick: Pulp never named it (the hidden-apps set).</summary>
    NotHidden,

    /// <summary>The policy plan hard-suspends it (a never-visible category, a browser, another home); a request never frees it.</summary>
    PolicySuspended,

    /// <summary>The owner blocked it; a block outranks a request.</summary>
    OwnerBlocked,

    /// <summary>The link's host is not one of the service's own domains (D301): a signed sender cannot aim a service's app at an arbitrary site.</summary>
    LinkHostNotAllowed,
}

/// <summary>
/// The streaming apps a locked hand-off may ever target, kept identical to Pulp's own
/// <c>ServiceCatalogue.Public</c> by a parity guard (G19). It is a second list on purpose (D296): the
/// stored hidden set is writable by any signed sibling, so membership in it cannot by itself be what
/// lets an app be lock-tasked.
/// </summary>
public static class LockedOpenCatalogue
{
    public static IReadOnlySet<string> Packages { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "com.netflix.mediaclient",
        "com.disney.disneyplus",
        "com.hulu.plus",
        "com.wbd.stream",
        "com.hbo.hbonow",
        "com.amazon.avod.thirdpartyclient",
        "com.cbs.app",
        "com.peacocktv.peacockandroid",
        "com.apple.atve.android.appletv",
        "com.apple.atve.androidtv.appletv",
    };

    /// <summary>
    /// The domains each service's own links live on (D301), kept equal to the <c>linkDomains</c> on Pulp's
    /// <c>ServiceCatalogue.Public</c> by guard G19. A host matches a domain when it is that domain or a
    /// subdomain of it. Public knowledge of where each service serves its title pages, not yet checked
    /// against the links the availability source returns; the first device run confirms every one.
    /// </summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> LinkDomains { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["com.netflix.mediaclient"] = ["netflix.com"],
            ["com.disney.disneyplus"] = ["disneyplus.com"],
            ["com.hulu.plus"] = ["hulu.com"],
            ["com.wbd.stream"] = ["max.com", "hbomax.com"],
            ["com.hbo.hbonow"] = ["max.com", "hbomax.com"],
            ["com.amazon.avod.thirdpartyclient"] = ["primevideo.com", "amazon.com"],
            ["com.cbs.app"] = ["paramountplus.com"],
            ["com.peacocktv.peacockandroid"] = ["peacocktv.com"],
            ["com.apple.atve.android.appletv"] = ["tv.apple.com", "apple.com"],
            ["com.apple.atve.androidtv.appletv"] = ["tv.apple.com", "apple.com"],
        };

    /// <summary>True when the host is, or is a subdomain of, one of the package's link domains.</summary>
    public static bool HostAllowed(string package, string host) =>
        LinkDomains.TryGetValue(package, out var domains)
        && domains.Any(domain =>
            host.Equals(domain, StringComparison.OrdinalIgnoreCase)
            || host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Reads the two extras into a <see cref="LockedOpenRequest"/>, or says it cannot.</summary>
public static class LockedOpenCodec
{
    private static readonly Regex PackageShape = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    public static bool TryRead(string? package, string? link, out LockedOpenRequest request)
    {
        request = new LockedOpenRequest(string.Empty, new Uri("https://invalid.example"));
        var id = package?.Trim() ?? string.Empty;
        if (!PackageShape.IsMatch(id)
            || string.IsNullOrWhiteSpace(link)
            || link.Length > LockedOpenHandoff.MaxLinkLength
            || !Uri.TryCreate(link.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        request = new LockedOpenRequest(id, uri);
        return true;
    }
}

/// <summary>
/// What a signed sender may ask Flyleaf to lock-task (D296). All of: a package-shaped id, outside the
/// owner's namespace, in the streaming catalogue, on that service's own link domain, ticked by the household (the stored hidden set),
/// not hard-suspended by the plan, and not blocked by the owner. Pure, so the receiver is a thin
/// reader of the device's facts.
/// </summary>
public static class LockedOpenGate
{
    public static LockedOpenRefusal Decide(
        LockedOpenRequest request,
        HiddenApps hidden,
        IReadOnlySet<string> ownerBlocked,
        IReadOnlySet<string> policySuspended)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(hidden);
        ArgumentNullException.ThrowIfNull(ownerBlocked);
        ArgumentNullException.ThrowIfNull(policySuspended);

        var package = request.Package;
        if (!LockedOpenCodec.TryRead(package, request.Link.AbsoluteUri, out _))
        {
            return LockedOpenRefusal.Unreadable;
        }

        if (package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal))
        {
            return LockedOpenRefusal.OwnerNamespace;
        }

        if (!LockedOpenCatalogue.Packages.Contains(package))
        {
            return LockedOpenRefusal.NotStreamingService;
        }

        if (!LockedOpenCatalogue.HostAllowed(package, request.Link.Host))
        {
            return LockedOpenRefusal.LinkHostNotAllowed;
        }

        if (!hidden.Contains(package))
        {
            return LockedOpenRefusal.NotHidden;
        }

        if (policySuspended.Contains(package))
        {
            return LockedOpenRefusal.PolicySuspended;
        }

        return ownerBlocked.Contains(package) ? LockedOpenRefusal.OwnerBlocked : LockedOpenRefusal.None;
    }
}
