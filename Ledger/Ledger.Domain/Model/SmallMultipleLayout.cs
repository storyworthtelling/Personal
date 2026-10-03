namespace Index.Domain.Model;

/// <summary>One panel's box inside the small-multiples grid, in the grid's own coordinates.</summary>
public readonly record struct PanelRect(float X, float Y, float Width, float Height);

/// <summary>A touch resolved to a panel: its index, the touch's X within the panel, and the panel's width.</summary>
public readonly record struct PanelHit(int Index, float LocalX, float PanelWidth);

/// <summary>
/// Mockup 6's "Each loan" grid: two columns reading across, so an odd last panel (the ninth, the
/// mortgage) sits alone in the LEFT column at the same half width as every other panel — it used to be
/// stretched across the page. One place decides the geometry; the painter draws from it and the touch
/// handler hit-tests against it, so the two cannot drift apart (the old touch handler divided by
/// <c>columnWidth + gap</c> where the painter placed panels at <c>col × (columnWidth + gap)</c>).
/// </summary>
public static class SmallMultipleLayout
{
    public const int Columns = 2;

    public static float PanelWidth(float totalWidth, float gap) => (totalWidth - gap) / Columns;

    public static PanelRect Panel(int index, float totalWidth, float panelHeight, float gap)
    {
        var width = PanelWidth(totalWidth, gap);
        var row = index / Columns;
        var column = index % Columns;
        return new PanelRect(column * (width + gap), row * (panelHeight + gap), width, panelHeight);
    }

    public static float TotalHeight(int count, float panelHeight, float gap) =>
        count <= 0 ? 0f : (count + Columns - 1) / Columns * (panelHeight + gap);

    /// <summary>The panel under (<paramref name="x"/>, <paramref name="y"/>), or null for the gutter
    /// between columns, the gap between rows, the empty half of an odd last row, or outside the grid.</summary>
    public static PanelHit? HitTest(float x, float y, int count, float totalWidth, float panelHeight, float gap)
    {
        if (count <= 0 || totalWidth <= gap || x < 0f || y < 0f)
        {
            return null;
        }

        var width = PanelWidth(totalWidth, gap);
        var row = (int)(y / (panelHeight + gap));
        var column = (int)(x / (width + gap));
        if (column >= Columns)
        {
            return null;
        }

        var index = row * Columns + column;
        if (index >= count)
        {
            return null;
        }

        var panel = Panel(index, totalWidth, panelHeight, gap);
        var localX = x - panel.X;
        var localY = y - panel.Y;
        return localX <= panel.Width && localY <= panel.Height
            ? new PanelHit(index, localX, panel.Width)
            : null;
    }
}
