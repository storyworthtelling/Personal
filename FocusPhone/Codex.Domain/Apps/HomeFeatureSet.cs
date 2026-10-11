namespace Codex.Domain.Apps;

/// <summary>
/// The apps featured as cover-out books on the home shelf, in slot order. The launcher shows
/// exactly <see cref="Slots"/> of them; the tray's "Feature on home" action swaps a chosen app
/// into one of the slots. Pure data (packages + order), so it is vault-backed for restore and
/// unit-tested without a device; the head maps each package to its painted cover / alias.
/// </summary>
/// <param name="Packages">The featured packages in slot order.</param>
public sealed record HomeFeatureSet(IReadOnlyList<string> Packages)
{
    // Canonical package ids for the default featured six (Gemini dropped; Google Home added).
    // Tile.cs adopts these when the home renderer is rebuilt (Phase D UI).
    public const string CommunicationPackage = "com.storyworthtelling.focusphone.communication";
    public const string CameraPackage = Policy.PolicyContext.PlatePackage;
    public const string AudioPackage = "com.storyworthtelling.focusphone.audio";
    public const string DrivePackage = "com.google.android.apps.docs";
    public const string GoogleHomePackage = "com.google.android.apps.chromecast.app";
    public const string MapsPackage = "com.google.android.apps.maps";

    public const int Slots = 6;

    /// <summary>The approved default home set on a fresh device (or when nothing is stored).</summary>
    public static HomeFeatureSet Default { get; } = new(
    [
        CommunicationPackage, CameraPackage, AudioPackage,
        DrivePackage, GoogleHomePackage, MapsPackage,
    ]);

    public bool Contains(string package) => Packages.Contains(package);

    /// <summary>
    /// Swaps a featured app for another, keeping the slot position and the slot count. The
    /// outgoing app must currently be featured; if the incoming app is already featured this is
    /// a no-op (a home shelf never holds the same app in two slots).
    /// </summary>
    public HomeFeatureSet Replace(string existingPackage, string newPackage, HiddenApps hidden)
    {
        ArgumentNullException.ThrowIfNull(hidden);
        // A hidden app is reachable only through Pulp: it never becomes a book on the home shelf.
        return hidden.Contains(newPackage) ? this : Replace(existingPackage, newPackage);
    }

    public HomeFeatureSet Replace(string existingPackage, string newPackage)
    {
        if (!Packages.Contains(existingPackage))
        {
            throw new ArgumentException(
                $"'{existingPackage}' is not featured on the home shelf.", nameof(existingPackage));
        }
        if (Packages.Contains(newPackage))
        {
            return this;
        }
        return new HomeFeatureSet(
            Packages.Select(p => p == existingPackage ? newPackage : p).ToList());
    }
}
