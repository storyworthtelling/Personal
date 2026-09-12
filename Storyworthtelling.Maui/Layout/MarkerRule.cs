namespace Storyworthtelling.Maui.Layout;

/// <summary>
/// One stroke of the rule, in the <see cref="MarkerRule"/> design space: a cubic from
/// <paramref name="StartX"/>,<paramref name="StartY"/> to <paramref name="EndX"/>,<paramref name="EndY"/>
/// through two control points.
/// </summary>
public readonly record struct MarkerStroke(
    float StartX, float StartY,
    float Control1X, float Control1Y,
    float Control2X, float Control2Y,
    float EndX, float EndY,
    float Width, float Opacity);

/// <summary>
/// The hand-drawn rule under a screen's in-page header — two overlapping marker strokes that lift
/// to the right, the way a line drawn quickly under a title does.
///
/// <para>It is <b>two</b> strokes rather than one, and that is the whole effect: a broad, nearly
/// opaque stroke with a thinner, much fainter one sitting just below and behind it, so the ink
/// reads as having bled rather than as a rule someone drew with a ruler. Shipping only the broad
/// stroke gives a coloured bar; shipping only the faint one gives an underline nobody notices.</para>
///
/// <para>The numbers are the mockups', kept in their own 100×12 space so the rule can be laid under
/// a header of any width without redrawing it: x is a percentage of the header's width and y is in
/// units of the rule's own height. Nothing here knows about MAUI, which is what lets the geometry be
/// tested rather than eyeballed on a device.</para>
/// </summary>
public static class MarkerRule
{
    /// <summary>The design space the strokes are written in. Width is a percentage.</summary>
    public const float DesignWidth = 100f;

    /// <summary>The rule's height in dp, and the y unit the strokes are written in.</summary>
    public const float HeightDp = 12f;

    /// <summary>The gap between the header's baseline and the rule.</summary>
    public const float TopGapDp = 4f;

    /// <summary>
    /// Back to front: the faint bleed first, the broad stroke over it. The order matters — drawn
    /// the other way the bleed sits on top and greys the stroke it is supposed to sit under.
    /// </summary>
    public static IReadOnlyList<MarkerStroke> Strokes { get; } =
    [
        new(0f, 10.2f, 30f, 9.8f, 62f, 6.4f, 100f, 4.2f, Width: 2f, Opacity: 0.41f),
        new(0f, 8.6f, 30f, 8.2f, 62f, 4.8f, 100f, 2.4f, Width: 5f, Opacity: 0.92f),
    ];

    /// <summary>
    /// The same stroke mapped onto a real header: x across <paramref name="width"/>, y down
    /// <paramref name="height"/>. The stroke width follows the height rather than the width,
    /// because a rule under a wide header should not get fatter — it should get longer.
    /// </summary>
    public static MarkerStroke Scale(MarkerStroke stroke, float width, float height)
    {
        var x = width / DesignWidth;
        var y = height / HeightDp;

        return stroke with
        {
            StartX = stroke.StartX * x,
            StartY = stroke.StartY * y,
            Control1X = stroke.Control1X * x,
            Control1Y = stroke.Control1Y * y,
            Control2X = stroke.Control2X * x,
            Control2Y = stroke.Control2Y * y,
            EndX = stroke.EndX * x,
            EndY = stroke.EndY * y,
            Width = stroke.Width * y,
        };
    }
}
