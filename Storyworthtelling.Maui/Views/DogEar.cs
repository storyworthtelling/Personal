using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui.Layout;
using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// Draws a folded top-right page corner, the same fold Post draws (DESIGN.md §7, §13): the page
/// curls to reveal a coloured underside, and the undo form adds a back-turn arrow. The shape is mockup
/// 17's, taken from <see cref="DogEarGeometry"/> so the native fold draws the same one.
///
/// <para>The geometry is Post's <c>FoldView</c>, member for member — a dog-ear is <b>two</b>
/// triangles, and shipping only the first is what once made this read as a coloured corner tag
/// rather than a folded page. Above the crease is the <b>hole</b>: the part of the page that is no
/// longer there, so what shows is whatever the fold is carrying. Below it is the <b>flap</b>: that
/// same corner turned over and lying back across the page, which is why it is paper — you are
/// looking at the back of the sheet — and why it is the half nearer the reader.</para>
/// </summary>
public sealed class FoldDrawable : IDrawable
{
    private readonly Func<string, Color> token;

    public FoldDrawable(Func<string, Color> token)
    {
        this.token = token;
    }

    /// <summary>What the corner is showing, or null for a flat corner.</summary>
    public DogEarFold? Fold { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Fold is not { } fold)
        {
            return;
        }

        float w = dirtyRect.Width, h = dirtyRect.Height;
        canvas.Antialias = true;

        // The hole: what the fold is carrying, seen through the missing corner.
        var hole = new PathF();
        hole.MoveTo(w, 0);
        hole.LineTo(0, 0);
        hole.LineTo(w, h);
        hole.Close();
        canvas.FillColor = token(DogEarPalette.TokenFor(fold.Hue));
        canvas.FillPath(hole, WindingMode.NonZero);

        // The flap: the corner turned over and lying back across the page, in paper. Mockup 17 draws
        // its loose tip lifted off the page (6,34 of 40) rather than running to the foot of the fold,
        // and flat — the shading that used to grade it is not in the drawing.
        var flap = new PathF();
        flap.MoveTo(0, 0);
        flap.LineTo(w, h);
        flap.LineTo(DogEarGeometry.Scale(DogEarGeometry.FlapTip.X, w), DogEarGeometry.Scale(DogEarGeometry.FlapTip.Y, h));
        flap.Close();
        canvas.FillColor = token("PaperDeep");
        canvas.FillPath(flap, WindingMode.NonZero);

        // The crease: a hairline between the halves.
        canvas.StrokeColor = token("Hairline");
        canvas.StrokeSize = DesignTokens.Shape.HairlineDp;
        canvas.DrawLine(0, 0, w, h);

        if (!fold.ShowsBackArrow)
        {
            return;
        }

        // The back-turn arrow, and ONLY on the undo fold: it is what says this one reverses the act
        // rather than turning to something waiting. Two strokes — an arc and a head — taken from the
        // mockup's own SVG through DogEarGeometry; it used to be a glyph set in a serif font.
        canvas.StrokeColor = token("PaperBright");
        canvas.StrokeSize = DogEarGeometry.Scale(DogEarGeometry.ArrowStroke, w);
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        var arc = new PathF();
        var points = DogEarGeometry.ArrowArc(ArrowSegments);
        arc.MoveTo(DogEarGeometry.Scale(points[0].X, w), DogEarGeometry.Scale(points[0].Y, h));
        for (var i = 1; i < points.Count; i++)
        {
            arc.LineTo(DogEarGeometry.Scale(points[i].X, w), DogEarGeometry.Scale(points[i].Y, h));
        }

        canvas.DrawPath(arc);

        var head = new PathF();
        var wings = DogEarGeometry.ArrowHead;
        head.MoveTo(DogEarGeometry.Scale(wings[0].X, w), DogEarGeometry.Scale(wings[0].Y, h));
        head.LineTo(DogEarGeometry.Scale(wings[1].X, w), DogEarGeometry.Scale(wings[1].Y, h));
        head.LineTo(DogEarGeometry.Scale(wings[2].X, w), DogEarGeometry.Scale(wings[2].Y, h));
        canvas.DrawPath(head);
    }

    /// <summary>How many straight segments the arrow's arc is flattened to: at 40dp a circle of radius
    /// 5.2 this fine is indistinguishable from the true curve.</summary>
    private const int ArrowSegments = 32;
}

/// <summary>
/// The corner itself: a <see cref="GraphicsView"/> that paints <see cref="DogEarState"/>'s verdict
/// and turns the fold when tapped.
///
/// <para>It holds no rules. What the corner shows, whether an undo is still open, and whether a
/// notice comes back when the window closes are all <see cref="DogEarState"/>'s, which is tested;
/// this is the painter and the tap target.</para>
///
/// <para><b>The window closes on a timer here rather than in the state</b>, because the state takes
/// time as an argument on purpose — it has no clock of its own, which is what made it testable. The
/// view is where a clock belongs.</para>
/// </summary>
public class DogEar : GraphicsView
{
    /// <summary>The fold's reach into the page: mockup 17's 40dp, square so the hole and the flap meet on
    /// the crease. It was 72dp, which on Accounts overlapped the status line and the first card.</summary>
    public const int SizeDp = DogEarGeometry.SizeDp;

    private readonly FoldDrawable painter;
    private readonly DogEarState state;
    private IDispatcherTimer? window;

    public DogEar(DogEarState state)
    {
        this.state = state;
        painter = new FoldDrawable(PaperResources.Colour);
        Drawable = painter;

        WidthRequest = SizeDp;
        HeightRequest = SizeDp;
        HorizontalOptions = LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        InputTransparent = false;

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => Turn();
        GestureRecognizers.Add(tap);

        Refresh();
    }

    /// <summary>
    /// Fires when a notice fold is turned — there is nothing to reverse, so the screen decides what
    /// opening the corner means. An undo fold reverses its act and does not raise this.
    /// </summary>
    public event Action<IReadOnlyList<DogEarNotice>>? NoticesOpened;

    /// <summary>
    /// Repaints from the state. Safe to call on every render: a screen states what is waiting and
    /// this works out whether the corner is folded at all.
    /// </summary>
    public void Refresh()
    {
        var fold = state.Fold(DateTimeOffset.Now);
        painter.Fold = fold;
        IsVisible = fold is not null;
        Invalidate();

        // One timer, always for the fold currently showing. Cancelling the outgoing one matters:
        // a timer left running from a fold being replaced would flatten its replacement early, so
        // the second undo of a quick pair would get a fraction of its window.
        window?.Stop();
        window = null;

        if (fold?.Expires is not { } expires)
        {
            return;
        }

        var remaining = expires - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero)
        {
            return;
        }

        window = Dispatcher.CreateTimer();
        window.Interval = remaining;
        window.IsRepeating = false;
        window.Tick += (_, _) => Refresh();
        window.Start();
    }

    private void Turn()
    {
        if (state.Turn(DateTimeOffset.Now))
        {
            Refresh();
            return;
        }

        // A notice fold turns TO something rather than reversing anything.
        if (state.Notices.Count > 0)
        {
            NoticesOpened?.Invoke(state.Notices);
        }
    }

}
