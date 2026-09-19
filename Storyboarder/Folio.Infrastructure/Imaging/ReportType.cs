using SkiaSharp;
using Storyworthtelling.Paper;

namespace Folio.Infrastructure.Imaging;

/// <summary>
/// Every size the report prints at, and where it comes from.
///
/// <para>The pages used to pick a number at each call site — 22, 30, 32, 40, 58, 72 — which were
/// pixels at 150 DPI and, converted, land at 10.6 / 14.4 / 15.4 / 19.2 / 27.8 / 34.6pt. None of them
/// was derived from anything. That is a second type scale, unregistered, and it drifts the way a
/// second copy of a palette drifts: invisibly, because nothing compares it to the system it belongs
/// to.</para>
///
/// <para>So each role names a rung of <see cref="PaperTypeScale"/>, the registered printed ladder
/// (DESIGN.md §19), and changing the printed scale means changing that ladder's one ratio and
/// watching every sheet in the suite move together.</para>
///
/// <para>The faces are §4's: Serif Black for the screen-or-cover title, Serif Semibold for section
/// heads, Serif Regular for the body a novelist reads in, and Sans for the apparatus — labels,
/// captions, the running foot — so that what is the book and what is the machinery are told apart by
/// the face rather than by the size.</para>
/// </summary>
public static class ReportType
{
    /// <summary>The cover's title. §4 gives every screen header Serif Black; a cover is the same job.</summary>
    public static SKFont Title() => Font(EmbeddedTypefaces.SerifBlack, PaperTypeScale.Display);

    /// <summary>A section head — the H2 that also becomes a contents entry.</summary>
    public static SKFont Section() => Font(EmbeddedTypefaces.SerifSemibold, PaperTypeScale.Title);

    /// <summary>A head inside a section (a character's name, a reader's name).</summary>
    public static SKFont Heading() => Font(EmbeddedTypefaces.SerifSemibold, PaperTypeScale.Heading);

    public static SKFont Body() => Font(EmbeddedTypefaces.SerifRegular, PaperTypeScale.Body);

    /// <summary>
    /// The manuscript itself, on the marked-up pages. The same face and rung as the body, named
    /// separately because it is the only type in the report that is the author's own words rather
    /// than the report's — and because <see cref="Folio.Domain.Markup.MarkupGeometry"/>'s line
    /// height, which decides how long the document is, is set against this size.
    /// </summary>
    public static SKFont Manuscript() => Font(EmbeddedTypefaces.SerifRegular, PaperTypeScale.Body);
    public static SKFont BodyBold() => Font(EmbeddedTypefaces.SerifSemibold, PaperTypeScale.Body);
    public static SKFont BodyItalic() => Font(EmbeddedTypefaces.SerifItalic, PaperTypeScale.Body);

    /// <summary>
    /// The line under a cover title — the draft and its length.
    ///
    /// <para>Sans, not Serif, and that is the drawing's decision rather than a slip: everything on
    /// the cover that is <i>about</i> the book rather than <i>of</i> it is set in the apparatus face,
    /// which is what keeps the title the only thing on the page speaking in the book's own voice.</para>
    /// </summary>
    public static SKFont Subtitle() => Font(EmbeddedTypefaces.SansRegular, PaperTypeScale.Body);

    /// <summary>The apparatus voice: a caption, a note under a head, a legend entry.</summary>
    public static SKFont Small() => Font(EmbeddedTypefaces.SansRegular, PaperTypeScale.Secondary);

    /// <summary>A field label, set in caps and letterspaced by the caller.</summary>
    public static SKFont Label() => Font(EmbeddedTypefaces.SansSemibold, PaperTypeScale.Label);

    /// <summary>The running foot's title.</summary>
    public static SKFont Running() => Font(EmbeddedTypefaces.SansRegular, PaperTypeScale.Label);

    /// <summary>The folio itself — figures, so the mono face, at the running foot's size.</summary>
    public static SKFont Folio() => Font(EmbeddedTypefaces.Mono, PaperTypeScale.Label);

    private static SKFont Font(SKTypeface typeface, float size) =>
        new(typeface, size) { Edging = SKFontEdging.Antialias, Subpixel = true };
}
