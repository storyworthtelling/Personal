using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Watching;

namespace Pulp.Watching;

/// <summary>
/// One Continue watching row as mockup 01 draws it (D150, D119): the poster in an artwork slot, then
/// the title with its "New episode" chip (D21), what's next, where it plays and how (D28, D29, D121),
/// and the thin pencil progress mark over its mono count. A hairline under each row. Tapping opens the
/// title page (D151); a long press opens the action card — both handed in by the page.
/// </summary>
public static class ContinueRowView
{
    /// <summary>The drawn poster, 58 × 87 — a 2:3 sheet, not the kit's square row window.</summary>
    private const int PosterWidthDp = 58;
    private const int PosterHeightDp = 87;

    /// <summary>The pencil mark's weight: thin, as drawn.</summary>
    private const int PencilDp = 3;

    public const string NewEpisodeChip = "New episode";

    public static View Build(Context context, WatchingRow row, Action onTap, Action onLongPress)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(row);

        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal, Clickable = true, LongClickable = true };
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        line.SetPadding(0, vertical, 0, vertical);
        line.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.ListRowMinHeightDp));
        line.Click += (_, _) => onTap();
        line.LongClick += (_, args) =>
        {
            args.Handled = true;
            onLongPress();
        };

        var posterHeight = PaperTheme.Dp(context, PosterHeightDp);
        var poster = new ArtworkWindow(context);
        poster.SetArtwork(CoverCache.Get(row.PosterPath, posterHeight));
        line.AddView(poster, new LinearLayout.LayoutParams(PaperTheme.Dp(context, PosterWidthDp), posterHeight)
        {
            MarginEnd = PaperTheme.Dp(context, DesignTokens.Space.X3),
        });

        var text = new LinearLayout(context) { Orientation = Orientation.Vertical };
        text.AddView(TitleLine(context, row));

        if (row.NextUp is { } next)
        {
            var nextView = PaperUi.Secondary(context, next);
            nextView.SetTextColor(PaperTheme.InkSoft);
            text.AddView(nextView);
        }

        if (row.Where is { } where)
        {
            text.AddView(PaperUi.Secondary(context, where));
        }

        if (row.Row.Progress is { } mark)
        {
            text.AddView(new PencilMark(context, ContinueRowText.Fraction(mark)), new LinearLayout.LayoutParams(
                ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, PencilDp))
            {
                TopMargin = PaperTheme.Dp(context, DesignTokens.Space.X2),
                BottomMargin = PaperTheme.Dp(context, DesignTokens.Space.X1),
            });

            var count = PaperUi.Mono(context, ContinueRowText.Count(mark));
            count.SetTextColor(PaperTheme.InkMuted);
            text.AddView(count);
        }

        line.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        column.AddView(line);
        column.AddView(Hairline(context));
        return column;
    }

    private static View TitleLine(Context context, WatchingRow row)
    {
        var titleLine = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        titleLine.SetGravity(GravityFlags.CenterVertical);

        var name = PaperUi.Body(context, row.Name);
        titleLine.AddView(name, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { Weight = 0 });

        if (row.Row.NewEpisode)
        {
            // The chip sits in the identity's wash with its deepest ink, as drawn — the identity's,
            // never a role's, so it reads as Pulp's own news rather than a state (D21, §9).
            var mark = Color.ParseColor(DesignTokens.Identity.Pulp.Mark);
            var chip = PaperUi.Label(context, NewEpisodeChip);
            chip.SetTextColor(Color.ParseColor(DesignTokens.Identity.Pulp.Deep));
            chip.Background = PaperUi.Fill(context, PaperTheme.Wash(mark), DesignTokens.Shape.RadiusCard);
            var padH = PaperTheme.Dp(context, DesignTokens.Space.X2);
            chip.SetPadding(padH, 0, padH, 0);
            titleLine.AddView(chip, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2),
            });
        }

        return titleLine;
    }

    private static View Hairline(Context context)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    /// <summary>
    /// The thin pencil progress mark (D150): a shade track with the identity's mark drawn along it
    /// to the count's share. A mark, not a meter — no percentage, no animation.
    /// </summary>
    private sealed class PencilMark : View
    {
        private readonly float fraction;
        private readonly Paint track = new() { AntiAlias = true, Color = PaperTheme.PaperShade };
        private readonly Paint lead = new() { AntiAlias = true, Color = Color.ParseColor(DesignTokens.Identity.Pulp.Mark) };

        public PencilMark(Context context, double fraction) : base(context) =>
            this.fraction = (float)Math.Clamp(fraction, 0d, 1d);

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var radius = Height / 2f;
            canvas.DrawRoundRect(0, 0, Width, Height, radius, radius, track);
            if (fraction > 0f)
            {
                canvas.DrawRoundRect(0, 0, Width * fraction, Height, radius, radius, lead);
            }
        }
    }
}
