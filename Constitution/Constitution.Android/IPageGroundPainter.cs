using Android.Views;

namespace Constitution;

/// <summary>
/// Puts a page's ground behind its well. The crinkled exam-table paper and its tear line
/// (D43) and the taped scraps (D41) are a later wave's painters; this is the named place they
/// plug in. Nothing is drawn by this wave.
/// </summary>
public interface IPageGroundPainter
{
    /// <summary>Returns the view the shell should place in the well: the well itself, or a host drawing the ground under it.</summary>
    View Paint(View well);
}

/// <summary>Draws nothing: the suite's own paper, as Settings stays for good (D71).</summary>
public sealed class PlainGroundPainter : IPageGroundPainter
{
    public static PlainGroundPainter Instance { get; } = new();

    public View Paint(View well) => well;
}
