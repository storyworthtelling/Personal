namespace Folio.Android.Pages;

/// <summary>
/// The action card's marks, as 18-unit path data.
///
/// <para>Stroked outlines rather than filled shapes, and path data rather than images, because each
/// one is drawn in whichever ink its row is written in — pine on the weighted row, ink on the
/// ordinary ones, muted on the quiet one — and an <c>ImageSource</c> cannot be re-tinted.</para>
/// </summary>
public static class Glyphs
{
    /// <summary>A sheet of paper with a folded corner: the report.</summary>
    public const string Page = "M4 2.5h7l3 3v10H4Z M11 2.5v3h3";

    /// <summary>The same sheet with a cross on it: a draft that does not exist yet.</summary>
    public const string NewPage = "M4 2.5h7l3 3v10H4Z M9 8.5v5 M6.5 11h5";

    /// <summary>Two columns side by side: what changed between drafts.</summary>
    public const string Compare = "M3 3h5v12H3Z M10 3h5v12h-5Z";

    /// <summary>A circling arrow: run it again.</summary>
    public const string Again = "M15 9a6 6 0 1 1-1.8-4.3 M15 2.5v3h-3";

    /// <summary>A square: stop.</summary>
    public const string Stop = "M4.5 4.5h9v9h-9Z";

    /// <summary>A bin, and the only glyph on the card that is never anything but quiet.</summary>
    public const string Bin = "M3.5 4.5h11 M7 4.5V3h4v1.5 M5 4.5 5.8 15h6.4L13 4.5";

    /// <summary>Books standing on a shelf: put it away.</summary>
    public const string Shelve = "M3 3v12 M6.5 3v12 M10 3.4l2 11.4 M14 4l1.6 10.6 M2 16h14";
}
