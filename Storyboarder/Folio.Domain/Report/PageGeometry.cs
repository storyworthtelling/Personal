namespace Folio.Domain.Report;

/// <summary>
/// The report page, in <b>points</b>.
///
/// <para>It used to be in pixels — 1240 × 1754, which is A4 at 150 DPI — because every page was
/// rasterised to a PNG and wrapped by PdfSharp. A PDF's user space is already 1/72 inch, so once the
/// pages are drawn through <c>VectorPdfSink</c> the pixel grid is gone and the geometry is the
/// page's own unit. Nothing here is converted at draw time; these <i>are</i> the numbers the canvas
/// takes.</para>
///
/// <para>The page is the tablet's own and wears the frame every tablet document shares (owner's decision): the board
/// is A4's height, as wide as the 445 × 594 glass is at A4's scale (630.8 points), so the binder tabs sit at the glass's
/// edge. The margins are the frame's — 54 at the left and foot, the 38-point tab gutter at the right — and the content
/// starts under the frame's header, which signs every page. The folio sits in the bottom margin, below the content box,
/// so a page that fills its column cannot run into its own page number.</para>
/// </summary>
public static class PageGeometry
{
    public const float PageHeight = 842f;
    public const float PageWidth = PageHeight * 445f / 594f;

    /// <summary>The shared frame's left margin (<c>SheetFrame.Margin</c>).</summary>
    public const float SideMargin = 54f;

    /// <summary>The shared frame's tab gutter (<c>BinderStrip.Gutter</c>): the content stops clear of the tabs.</summary>
    public const float RightMargin = 38f;

    /// <summary>Under the frame's header (the book's title, "Folio" in the owner's hand, the rule) and its clear space.</summary>
    public const float TopMargin = 98f;

    public const float BottomMargin = 54f;

    public const float ContentLeft = SideMargin;
    public const float ContentRight = PageWidth - RightMargin;
    public const float ContentWidth = ContentRight - ContentLeft;
    public const float ContentTop = TopMargin;
    public const float ContentBottom = PageHeight - BottomMargin;
    public const float ContentHeight = ContentBottom - ContentTop;

    /// <summary>Body line spacing; the drawn pages set 1.35 on the 12pt body.</summary>
    public const float LineSpacing = 1.35f;

    /// <summary>The rule under a page's label: 1.5pt of the Document Action hue.</summary>
    public const float RuleHeight = 1.5f;
    public const float RuleGapAbove = 7f;
    public const float RuleGapBelow = 14f;

    /// <summary>The hairline that separates blocks within a page, and its air.</summary>
    public const float HairlineHeight = 1f;
    public const float HairlineGap = 14f;

    public const float BulletIndent = 16f;

    /// <summary>
    /// The three amounts of air the report puts between things, named rather than typed.
    ///
    /// <para>They were 18–20, 12 and 8 — <b>pixels</b> at 150 DPI, which is 8.6, 5.8 and 3.8 points.
    /// Carried across as the same figures they would have been twice the air they were drawn with,
    /// and the report would have reflowed for a reason nobody could see in the diff. So the unit
    /// change is done here, once, where it can be read.</para>
    /// </summary>
    public const float SectionGap = 9f;
    public const float BlockGap = 6f;
    public const float TightGap = 4f;

    /// <summary>The per-scene evidence image block, portrait, inside the content column.</summary>
    public const float SceneImageWidth = 269f;
    public const float SceneImageHeight = 359f;

    /// <summary>The tension-over-chapters pacing chart.</summary>
    public const float PacingChartHeight = 106f;
}
