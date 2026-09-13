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
/// <para>The margins are the drawn ones (52.8 / 48 / 62.4), which leave a 489.4 × 731.6 content
/// column — the measure the report's type ladder was set against. The folio sits 30pt off the foot,
/// below the content box rather than inside it, so a page that fills its column cannot run into its
/// own page number.</para>
/// </summary>
public static class PageGeometry
{
    public const float PageWidth = 595f;    // A4
    public const float PageHeight = 842f;

    public const float SideMargin = 52.8f;
    public const float TopMargin = 48f;
    public const float BottomMargin = 62.4f;

    /// <summary>Baseline of the running foot, measured up from the foot of the sheet.</summary>
    public const float FolioBaseline = 30f;

    public const float ContentLeft = SideMargin;
    public const float ContentRight = PageWidth - SideMargin;
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
