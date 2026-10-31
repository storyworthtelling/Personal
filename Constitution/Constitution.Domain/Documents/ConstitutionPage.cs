namespace Constitution.Domain.Documents;

/// <summary>
/// The tablet's own page: 445 by 594 points, the native 3:4 of the reMarkable (D13), so a point on
/// the page is a point on the glass and nothing is scaled or cropped. Every document begins its pages
/// at this size through <c>VectorPdfSink.BeginPage</c> and never at a number of its own.
/// </summary>
public static class ConstitutionPage
{
    public const float Width = 445f;
    public const float Height = 594f;

    public const float MarginLeft = 36f;
    public const float MarginRight = 36f;
    public const float MarginBottom = 36f;

    /// <summary>The header rule: the one coloured line on a page, in the document hue.</summary>
    public const float HeaderRuleTop = 64f;
    public const float HeaderRuleWeight = 1.5f;

    /// <summary>The page title sits above the rule, on the left margin, with the target clearance between.</summary>
    private const float TitleTop = 24f;
    private const float TitleHeight = 32f;

    /// <summary>Room kept between the header rule and the first target, the same eight points as any other.</summary>
    public const float HeaderClearance = InkTargets.MinimumClearance;

    public static InkRect TitleBounds => new(MarginLeft, TitleTop, Width - MarginLeft - MarginRight, TitleHeight);

    /// <summary>The room a target may use: under the header's clear space, inside the margins.</summary>
    public static InkRect Body
    {
        get
        {
            var top = HeaderRuleTop + HeaderClearance;
            return new InkRect(MarginLeft, top, Width - MarginLeft - MarginRight, Height - MarginBottom - top);
        }
    }
}
