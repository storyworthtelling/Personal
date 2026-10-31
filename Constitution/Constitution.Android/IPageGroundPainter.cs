using Android.Views;

namespace Constitution;

/// <summary>
/// Puts a page's ground behind its well: the crinkled exam-table paper and its tear line (D43;
/// the crinkle painter lives in Scrapbook/) or the suite's plain paper (D71, <see cref="PlainGroundPainter"/>).
/// Which one a page gets is decided once, in <c>ConstitutionAreaBase</c>, from the page's
/// <c>PageGround</c>. The ground fills the whole well and is never behind the binder gutter.
/// </summary>
public interface IPageGroundPainter
{
    /// <summary>
    /// Returns the view the shell should place in the well: <paramref name="well"/> itself, or a host
    /// drawing the ground under it. The well's content fills the host edge to edge.
    /// </summary>
    View Paint(View well);
}

/// <summary>Draws nothing: the suite's own paper, as Settings stays for good (D71).</summary>
public sealed class PlainGroundPainter : IPageGroundPainter
{
    public static PlainGroundPainter Instance { get; } = new();

    public View Paint(View well) => well;
}
