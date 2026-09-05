using Android.Graphics;
using Android.Graphics.Drawables;

namespace Flyleaf;

/// <summary>
/// Supplies the per-book art the <see cref="BookshelfView"/> paints, keyed by package id, so the
/// renderer stays free of PackageManager / notification plumbing (the Activity provides those).
/// </summary>
public interface IShelfArt
{
    /// <summary>The bespoke painted cover for a featured book, or null to draw the cloth fallback.</summary>
    Bitmap? Cover(string packageKey);

    /// <summary>The app's launcher icon — the small square at a spine's base, and fallback cover art.</summary>
    Drawable? Icon(string packageKey);

    /// <summary>A human label: the spine title, the fallback bookplate, and the a11y description.</summary>
    string Label(string packageKey);

    /// <summary>The book's cloth/binding color (ARGB int) — its spine and fallback cover ground.</summary>
    int Cloth(string packageKey);

    /// <summary>True while the app has an active notification, so a bookmark tab is drawn.</summary>
    bool HasNotification(string packageKey);
}
