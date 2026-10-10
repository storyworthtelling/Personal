using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Discover;

namespace Pulp.Discover;

/// <summary>
/// A pile's front poster with the rest fanned behind it (D219, mockup 03's <c>.deck</c>): at most two
/// plain paper cards, each turned a little further and set down-left of the last, a hairline-framed
/// poster on top, and — when the card is vetoed — the poster dimmed with one ink-blue stroke across it
/// (D220). The poster is fetched off the UI thread by <see cref="Pulp.Components.Posters"/>; until it lands the
/// front card is the same plain paper as the ones behind.
/// </summary>
internal sealed class PosterDeck : FrameLayout
{
    // Mockup 03: the backs sit at -3 and -6 degrees.
    private static readonly float[] Turns = [-3f, -6f];

    /// <summary>The poster's width: the drawn 132px as the nearest tokens (X24 + X8).</summary>
    public static int WidthDp => DesignTokens.Space.X24 + DesignTokens.Space.X8;

    /// <summary>A poster is 2:3.</summary>
    public static int HeightDp => WidthDp * 3 / 2;

    private readonly ImageView poster;
    private readonly List<View> backs = [];
    private readonly Paint stroke;
    private bool struck;

    public PosterDeck(Context context) : base(context)
    {
        SetClipChildren(false);
        SetClipToPadding(false);
        SetWillNotDraw(false);

        for (var i = DiscoverPagePile.MaxFannedBehind; i >= 1; i--)
        {
            var back = new View(context)
            {
                Background = PaperUi.Outline(context, PaperTheme.PaperDeep, PaperTheme.Hairline, 0),
                Rotation = Turns[i - 1],
                TranslationX = -PaperTheme.Dp(context, i == 1 ? DesignTokens.Space.X2 : DesignTokens.Space.X3),
                TranslationY = PaperTheme.Dp(context, i == 1 ? DesignTokens.Space.X1 : DesignTokens.Space.X2),
            };
            backs.Insert(0, back);
            AddView(back, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        }

        poster = new ImageView(context);
        poster.SetScaleType(ImageView.ScaleType.CenterCrop);
        poster.Background = PaperUi.Outline(context, PaperTheme.PaperDeep, PaperTheme.Hairline, 0);
        var frame = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp);
        poster.SetPadding(frame, frame, frame, frame);
        AddView(poster, new LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        stroke = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.InkBlue,
            StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.WarningRuleDp),
            StrokeCap = Paint.Cap.Round,
        };
    }

    /// <summary>The front card's poster address; the image arrives when it has been fetched.</summary>
    public string? PosterUrl { get; private set; }

    public void Show(string? posterUrl, int behind, bool isStruck)
    {
        for (var i = 0; i < backs.Count; i++)
        {
            backs[i].Visibility = i < behind ? ViewStates.Visible : ViewStates.Gone;
        }

        struck = isStruck;
        poster.Alpha = struck ? DesignTokens.Alpha.DisabledOpacity : 1f;
        if (!string.Equals(PosterUrl, posterUrl, StringComparison.Ordinal))
        {
            PosterUrl = posterUrl;
            poster.SetImageBitmap(null);
        }

        Invalidate();
    }

    public void SetPoster(string url, Bitmap? bitmap)
    {
        if (string.Equals(PosterUrl, url, StringComparison.Ordinal))
        {
            poster.SetImageBitmap(bitmap);
        }
    }

    protected override void DispatchDraw(Canvas canvas)
    {
        base.DispatchDraw(canvas);
        if (!struck)
        {
            return;
        }

        // Mockup 03's veto: one ink-blue stroke rising across the poster, a little past its edges.
        var overhang = PaperTheme.Dp(Context!, DesignTokens.Space.X1);
        canvas.DrawLine(-overhang, Height * 0.72f, Width + overhang, Height * 0.28f, stroke);
    }
}

/// <summary>
/// A title struck through in ink-blue (D220, mockup 03's <c>.t.struck</c>): the text muted, the line
/// drawn here in the pen's colour, since the platform's strike-through only takes the text colour.
/// </summary>
internal sealed class StruckTitleView : TextView
{
    private readonly Paint line;

    public StruckTitleView(Context context) : base(context)
    {
        line = new Paint(PaintFlags.AntiAlias)
        {
            Color = PaperTheme.InkBlue,
            StrokeWidth = PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp),
        };
    }

    public bool Struck { get; set; }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (!Struck || Layout is not { } layout)
        {
            return;
        }

        for (var i = 0; i < layout.LineCount; i++)
        {
            var baseline = layout.GetLineBaseline(i) + TotalPaddingTop;
            var middle = baseline - (Paint!.TextSize * 0.3f);
            var left = layout.GetLineLeft(i) + TotalPaddingLeft;
            var right = layout.GetLineRight(i) + TotalPaddingLeft;
            canvas.DrawLine(left, middle, right, middle, line);
        }
    }
}
