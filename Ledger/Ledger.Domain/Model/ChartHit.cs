namespace Index.Domain.Model;

/// <summary>
/// R-G24 (mockup 6: "Touching a bar or a panel shows that week's date and balance"): which recorded
/// week a touch landed on, as a pure function of the touch's X, the width of the PLOT it landed in
/// and the number of weeks drawn across it.
///
/// <para>Both History charts used to answer this separately. The stacked bars divided the touch by the
/// view's full width; the small multiples ignored X entirely and reported the last week for every
/// tap — nothing failed, the caption just always said "now". One mapping, two shapes of mark:</para>
/// <list type="bullet">
/// <item><see cref="AtBar"/> — a bar owns an equal SLOT of the width, so the touch falls in a slot.</item>
/// <item><see cref="AtPoint"/> — a line's first point sits on the left edge and its last on the
/// right, so the touch finds the nearest point.</item>
/// </list>
/// Both return null for a touch outside <c>[0, width]</c> (the axis gutter beside the plot is not a
/// week), and for nothing to hit.
/// </summary>
public static class ChartHit
{
    public static int? AtBar(float x, float width, int count)
    {
        if (!Inside(x, width, count))
        {
            return null;
        }

        return Math.Clamp((int)(x / (width / count)), 0, count - 1);
    }

    public static int? AtPoint(float x, float width, int count)
    {
        if (!Inside(x, width, count))
        {
            return null;
        }

        return count == 1
            ? 0
            : Math.Clamp((int)Math.Round(x / width * (count - 1), MidpointRounding.AwayFromZero), 0, count - 1);
    }

    private static bool Inside(float x, float width, int count) =>
        count > 0 && width > 0f && x >= 0f && x <= width; // NaN fails both comparisons
}
