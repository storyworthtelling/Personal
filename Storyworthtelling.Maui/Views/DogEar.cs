using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Graphics;
using Storyworthtelling.Maui.State;

namespace Storyworthtelling.Maui.Views;

/// <summary>
/// Draws a folded top-right page corner, the same fold Post draws (DESIGN.md §7, §13): the page
/// curls to reveal a coloured underside, and the undo form adds a back-turn arrow.
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
    /// <summary>The crease's shadow, as a fraction of full ink. Darkest where the halves meet.</summary>
    private const float CreaseAlpha = 60f / 255f;

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

        // The flap: the corner reflected across the crease. For a square fold the corner at (w,0)
        // lands at (0,h), which is what makes the turned-down triangle the mirror of the hole
        // rather than a second copy of it.
        var flap = new PathF();
        flap.MoveTo(0, 0);
        flap.LineTo(w, h);
        flap.LineTo(0, h);
        flap.Close();

        // Lit along the crease and falling away toward the loose tip: a flat fill reads as a
        // triangle sitting on the page, a graded one as paper lifting off it. The stops run
        // perpendicular to the crease, so the light follows the fold. MAUI states a gradient in
        // relative coordinates and maps them onto the rect passed to SetFillPaint.
        canvas.SetFillPaint(
            new LinearGradientPaint(
                [
                    new PaintGradientStop(0f, token("PaperBright")),
                    new PaintGradientStop(1f, token("PaperShade")),
                ],
                new Point(0.5, 0.5),
                new Point(0, 1)),
            dirtyRect);
        canvas.FillPath(flap, WindingMode.NonZero);

        // The flap's own edge, so it separates from the page it is lying on.
        canvas.StrokeColor = token("Hairline");
        canvas.StrokeSize = DesignTokens.Shape.HairlineDp;
        canvas.DrawPath(flap);

        // The crease itself. Drawn through the canvas alpha rather than a mixed colour, so no
        // value is invented here that the token file does not carry.
        canvas.SaveState();
        canvas.Alpha = CreaseAlpha;
        canvas.StrokeColor = token("Ink");
        canvas.StrokeSize = 2 * DesignTokens.Shape.HairlineDp;
        canvas.DrawLine(0, 0, w, h);
        canvas.RestoreState();

        if (!fold.ShowsBackArrow)
        {
            return;
        }

        // A small back-turn arrow, and ONLY on the undo fold: it is what says this one reverses
        // the act rather than turning to something waiting.
        canvas.Font = new Font("SerifSemibold", FontWeights.SemiBold, FontStyleType.Normal);
        canvas.FontSize = ArrowSize;
        canvas.FontColor = token("Paper");
        canvas.DrawString("↶", w * 0.66f, h * 0.42f, HorizontalAlignment.Center);
    }

    /// <summary>The arrow's size. Not a type-size token: it is a drawn mark, not text on a page.</summary>
    private const float ArrowSize = 18f;
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
    /// <summary>The fold's reach into the page. Square, so the flap mirrors the hole exactly.</summary>
    public const int SizeDp = 72;

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
