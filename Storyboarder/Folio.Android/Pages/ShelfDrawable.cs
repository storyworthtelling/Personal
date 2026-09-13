using Folio.Domain.Shelf;
using Microsoft.Maui.Graphics;

namespace Folio.Android.Pages;

/// <summary>
/// One shelf, painted: the spines standing on it, the bronze bookends that bracket a series, and the
/// drawn ledge underneath.
///
/// <para>Drawn rather than composed from views because of the <b>title down the spine</b>. That is
/// the one place in the suite where rotated type is right — it is what a real spine does — and §9's
/// ban on rotating labels is about <i>tab</i> labels, which is a different thing. Rotating a MAUI
/// label to do it would fight the layout for no gain; here it is one canvas transform.</para>
///
/// <para>The ledge and bookends are marker-and-pencil, the same hand as the desk, so the two areas
/// are obviously the same world rather than two apps stitched together.</para>
/// </summary>
public sealed class ShelfDrawable : IDrawable
{
    public const float LedgeHeightDp = 14f;
    private const float SpineLabelSizeDp = 12f;

    public ShelfRow? Row { get; set; }

    /// <summary>The tallest a spine can stand, so every ledge on the screen lines up.</summary>
    public float TallestDp { get; set; } = SpineSizing.MaxHeightDp;

    /// <summary>The height this drawable needs: the tallest spine plus the ledge under it.</summary>
    public float HeightDp => TallestDp + LedgeHeightDp;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Row is null || Row.Books.Count == 0)
        {
            return;
        }

        var ledgeTop = TallestDp;
        var x = dirtyRect.Left;

        if (Row.HasBookends)
        {
            DrawBookend(canvas, x, ledgeTop, flipped: false);
            x += ShelfComposer.BookendWidthDp;
        }

        foreach (var book in Row.Books)
        {
            DrawSpine(canvas, book, x, ledgeTop);
            x += book.WidthDp + ShelfRow.GapDp;
        }

        if (Row.HasBookends)
        {
            DrawBookend(canvas, x - ShelfRow.GapDp, ledgeTop, flipped: true);
        }

        DrawLedge(canvas, dirtyRect.Left, ledgeTop, dirtyRect.Width);
    }

    private static void DrawSpine(ICanvas canvas, ShelfBook book, float x, float ledgeTop)
    {
        float height = book.HeightDp;
        var top = ledgeTop - height;
        var cloth = BookCloth.For(book.Title);

        canvas.SaveState();
        canvas.SetFillPaint(
            new LinearGradientPaint
            {
                StartColor = cloth.Top,
                EndColor = cloth.Bottom,
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
            },
            new RectF(x, top, book.WidthDp, height));
        canvas.FillRoundedRectangle(x, top, book.WidthDp, height, 2, 2, 0, 0);
        canvas.RestoreState();

        // The hinge line down the spine's binding edge, as in the frame.
        canvas.StrokeSize = 1;
        canvas.StrokeColor = cloth.Hinge;
        canvas.DrawLine(x + 0.5f, top + 8, x + 0.5f, ledgeTop - 8);

        DrawSpineTitle(canvas, book.Title, x, top, book.WidthDp, height, cloth.Lettering);
    }

    /// <summary>
    /// The title, read the way a spine is read: rotated a quarter turn, running down. Clipped to the
    /// spine so a long title on a narrow book is cut off by the book rather than printed across its
    /// neighbour.
    /// </summary>
    private static void DrawSpineTitle(
        ICanvas canvas, string title, float x, float top, float width, float height, Color ink)
    {
        canvas.SaveState();
        canvas.ClipRectangle(x, top, width, height);
        canvas.Translate(x + (width / 2f), top + (height / 2f));
        canvas.Rotate(90);

        canvas.FontColor = ink;
        canvas.FontSize = SpineLabelSizeDp;
        canvas.Font = Microsoft.Maui.Graphics.Font.DefaultBold;
        canvas.DrawString(
            title, -height / 2f, -width / 2f, height, width,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.RestoreState();
    }

    // A bronze bookend: the L that stands against the end of a row, drawn rather than rendered.
    private static void DrawBookend(ICanvas canvas, float x, float ledgeTop, bool flipped)
    {
        const float w = ShelfComposer.BookendWidthDp;
        const float h = 84f;
        var top = ledgeTop - h;

        float At(float u) => x + (flipped ? w - u : u);

        var path = new PathF();
        path.MoveTo(At(12.6f), top + 4);
        path.QuadTo(At(13.4f), top + 42, At(13f), top + 80);
        path.LineTo(At(2.4f), top + 80);
        path.QuadTo(At(2f), top + 77, At(2.6f), top + 74);
        path.LineTo(At(9f), top + 74);
        path.QuadTo(At(8.6f), top + 40, At(9.2f), top + 4);
        path.Close();

        canvas.FillColor = BookCloth.Bronze.WithAlpha(0.85f);
        canvas.FillPath(path);
        canvas.StrokeColor = BookCloth.BronzeEdge;
        canvas.StrokeSize = 1.3f;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.DrawPath(path);
    }

    private static void DrawLedge(ICanvas canvas, float x, float top, float width)
    {
        var right = x + width;
        var middle = x + (width / 2f);

        var face = new PathF();
        face.MoveTo(x + 2, top + 5);
        face.QuadTo(middle, top + 1, right - 2, top + 5);
        face.LineTo(right - 2, top + 10);
        face.QuadTo(middle, top + 14, x + 2, top + 10);
        face.Close();

        canvas.FillColor = BookCloth.Bronze.WithAlpha(0.55f);
        canvas.FillPath(face);

        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = BookCloth.BronzeEdge;
        canvas.StrokeSize = 2.2f;
        var edge = new PathF();
        edge.MoveTo(x + 2, top + 5);
        edge.QuadTo(middle, top + 1, right - 2, top + 5);
        canvas.DrawPath(edge);

        canvas.StrokeColor = BookCloth.BronzeEdge.WithAlpha(0.5f);
        canvas.StrokeSize = 1.2f;
        var under = new PathF();
        under.MoveTo(x + 2, top + 10.5f);
        under.QuadTo(middle, top + 14.5f, right - 2, top + 10.5f);
        canvas.DrawPath(under);
    }
}
