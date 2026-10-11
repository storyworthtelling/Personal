using System.Text.Json;
using System.Text.RegularExpressions;
using Codex.Domain.Policy;

namespace Codex.Domain.Apps;

/// <summary>
/// The wire contract Pulp sends Flyleaf the hidden apps over (P12, D8, D92, D93). Pulp, the
/// household's TV and film guide, publishes the package ids of exactly the streaming services the
/// household ticked there; Flyleaf keeps those out of its tray so every arrival is at a specific
/// title, never an app's home feed.
///
/// An <b>explicit</b> broadcast naming this receiver, guarded by a signature-level permission the
/// launcher declares: naming the component means nothing else can intercept the set, and the
/// permission means nothing else can send one (any app could otherwise hide the owner's apps). It
/// costs nothing, because every head shares one keystore for the update channel.
///
/// The payload crosses as exact text, a JSON array of package ids under <see cref="PackagesExtra"/>.
/// Pulp's <c>HiddenAppsContractTests</c> reads this file by content, so neither half can move alone.
/// </summary>
public static class HiddenAppsHandoff
{
    public const string Package = "com.storyworthtelling.focusphone.launcher";

    public const string Receiver = "com.storyworthtelling.focusphone.launcher.HiddenAppsReceiver";

    public const string Action = "com.storyworthtelling.focusphone.launcher.action.HIDDEN_APPS";

    /// <summary>Declared by Flyleaf at protectionLevel="signature"; Pulp must hold it to send.</summary>
    public const string Permission = "com.storyworthtelling.focusphone.launcher.RECEIVE_HIDDEN";

    public const string PackagesExtra = "packages";

    /// <summary>The preferences file the set persists in; a storage key, kept stable.</summary>
    public const string PreferencesName = "hiddenApps";
}

/// <summary>
/// The packages Flyleaf keeps out of its tray. Built only through <see cref="Of"/>, which keeps what
/// looks like a package id, once each, in a fixed order, and refuses anything under the owner's own
/// namespace: a signed sender is still not allowed to hide Post, Plate or the launcher itself.
/// </summary>
public sealed class HiddenApps
{
    private static readonly Regex PackageShape = new(
        @"^[A-Za-z][A-Za-z0-9_]*(\.[A-Za-z][A-Za-z0-9_]*)+$", RegexOptions.Compiled);

    private readonly HashSet<string> members;

    private HiddenApps(IReadOnlyList<string> packages)
    {
        Packages = packages;
        members = new HashSet<string>(packages, StringComparer.Ordinal);
    }

    /// <summary>Nothing hidden: the state before Pulp has ever spoken (D93 keeps the last set after).</summary>
    public static HiddenApps None { get; } = new([]);

    public IReadOnlyList<string> Packages { get; }

    public bool IsEmpty => Packages.Count == 0;

    public bool Contains(string package) => members.Contains(package);

    public static HiddenApps Of(IEnumerable<string> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var kept = packages
            .Select(package => package?.Trim() ?? string.Empty)
            .Where(package => PackageShape.IsMatch(package))
            .Where(package => !package.StartsWith(PolicyContext.OwnerPackagePrefix, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(package => package, StringComparer.Ordinal)
            .ToList();
        return kept.Count == 0 ? None : new HiddenApps(kept);
    }
}

/// <summary>The text form of a <see cref="HiddenApps"/>: a JSON array of package ids.</summary>
public static class HiddenAppsCodec
{
    public static string Encode(HiddenApps hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        return JsonSerializer.Serialize(hidden.Packages);
    }

    /// <summary>
    /// True only for a well-formed list of strings (an empty list included: it means "unhide
    /// everything"). Absent or garbled text is false, so a receiver can tell "Pulp said none" from
    /// "something unreadable arrived" and never clobber the stored set with the second.
    /// </summary>
    public static bool TryDecode(string? text, out HiddenApps hidden)
    {
        hidden = HiddenApps.None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var packages = new List<string>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.String)
                {
                    return false;
                }

                packages.Add(element.GetString()!);
            }

            hidden = HiddenApps.Of(packages);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>Absent or garbled text is the empty set.</summary>
    public static HiddenApps Decode(string? text) =>
        TryDecode(text, out var hidden) ? hidden : HiddenApps.None;
}
