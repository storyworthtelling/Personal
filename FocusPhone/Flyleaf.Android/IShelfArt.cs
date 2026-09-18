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

    /// <summary>
    /// True when this book's binding is light enough that the plate's marks — rule,
    /// corner flourishes, window keyline and title — have to be drawn in ink instead
    /// of paper (DESIGN.md §12).
    /// </summary>
    bool MarksInvertToInk(string packageKey);

    /// <summary>True while the app has an active notification, so a bookmark tab is drawn.</summary>
    bool HasNotification(string packageKey);

    /// <summary>
    /// The bookmark ribbon's color (ARGB int) for a notifying book — a Paper &amp; Ink role hue
    /// (DESIGN.md §9) chosen by that app's highest-priority active notification, not the book's
    /// own cloth. A cloth-colored tab on a cloth-colored book nearly vanishes on some spines; a
    /// priority-coded one reads at a glance regardless of which book it is on.
    /// </summary>
    int NotificationHue(string packageKey);
}
