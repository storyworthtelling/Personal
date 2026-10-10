using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Catalogue;
using Pulp.Domain.Library;
using Pulp.Domain.Title;

// Android.Graphics.Path, not System.IO.Path — the implicit usings bring both in.
using Path = Android.Graphics.Path;

namespace Pulp.Title;

/// <summary>
/// The season accordion (mockup 02, D159, D217): one Serif header per season with its count, the
/// current season open. An episode is its number and its title only — no still anywhere (D37) — and
/// the next one is listed like any other, under the identity's highlighter wash. A tap opens the
/// official synopsis in place; a long-press asks the page to mark it (D160). Watched episodes take a
/// pine tick and fade their title to the muted ink.
///
/// <para>Not the kit's <c>AccordionPicker</c>: that is a single-choice field that closes on a pick,
/// while this lists every season and opens one or more at a time.</para>
/// </summary>
internal sealed class SeasonAccordion : LinearLayout
{
    private readonly Action<CatalogueEpisode> onLongPress;
    private readonly HashSet<int> open = [];

    public SeasonAccordion(Context context, Action<CatalogueEpisode> onLongPress) : base(context)
    {
        this.onLongPress = onLongPress ?? throw new ArgumentNullException(nameof(onLongPress));
        Orientation = Orientation.Vertical;
    }

    /// <summary>
    /// Lays the seasons out. <paramref name="openSeason"/> opens on the first paint only; afterwards the
    /// viewer's own opening and closing stands across repaints (a mark repaints the ticks, not the folds).
    /// </summary>
    public void Show(CatalogueTitle show, IReadOnlySet<EpisodeRef> watched, EpisodeRef? next, int openSeason)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(watched);

        if (open.Count == 0)
        {
            open.Add(openSeason);
        }

        RemoveAllViews();
        foreach (var season in show.Seasons.OrderBy(season => season.Number))
        {
            AddView(Rule(Context!, PaperTheme.Hairline));
            AddView(Header(season, watched));
            if (open.Contains(season.Number))
            {
                foreach (var episode in season.Episodes.OrderBy(episode => episode.Episode))
                {
                    AddView(Rule(Context!, PaperTheme.PaperDeep));
                    AddView(EpisodeRow(episode, watched.Contains(episode.Episode), episode.Episode == next));
                }
            }
        }
    }

    private View Header(CatalogueSeason season, IReadOnlySet<EpisodeRef> watched)
    {
        var context = Context!;
        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));

        var heading = PaperUi.Heading(context, TitlePageText.SeasonHeading(season));
        row.AddView(heading, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var count = season.Episodes.Count(episode => watched.Contains(episode.Episode));
        var summary = PaperUi.Label(context, TitlePageText.SeasonSummary(season.Episodes.Count, count));
        summary.Typeface = PaperTheme.SansRegular(context);
        row.AddView(summary);

        var isOpen = open.Contains(season.Number);
        var chevron = new ChevronView(context, up: isOpen);
        var size = PaperTheme.Dp(context, DesignTokens.Space.X4);
        row.AddView(chevron, new LayoutParams(size, size) { MarginStart = PaperTheme.Dp(context, DesignTokens.Space.X2) });

        row.ContentDescription = $"{TitlePageText.SeasonHeading(season)}, {(isOpen ? "open" : "closed")}";
        row.Click += (_, _) =>
        {
            if (!open.Remove(season.Number))
            {
                open.Add(season.Number);
            }

            Repaint?.Invoke();
        };
        return row;
    }

    /// <summary>Raised when a fold changed, so the page repaints with what it now knows.</summary>
    public event Action? Repaint;

    private View EpisodeRow(CatalogueEpisode episode, bool isWatched, bool isNext)
    {
        var context = Context!;
        var column = new LinearLayout(context) { Orientation = Orientation.Vertical };
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X2);
        var inset = isNext ? PaperTheme.Dp(context, DesignTokens.Space.X2) : 0;
        column.SetPadding(inset, vertical, inset, vertical);
        column.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        if (isNext)
        {
            column.Background = PaperUi.Fill(context, PaperTheme.Wash(Identity), DesignTokens.Shape.RadiusButton / 2);
        }

        var line = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        line.SetGravity(GravityFlags.CenterVertical);

        var number = PaperUi.Mono(context, episode.Episode.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        number.SetTextColor(PaperTheme.InkMuted);
        number.SetMinWidth(PaperTheme.Dp(context, DesignTokens.Space.X6));
        line.AddView(number);

        var name = PaperUi.Body(context, string.IsNullOrWhiteSpace(episode.Name) ? $"Episode {episode.Episode.Number}" : episode.Name.Trim());
        if (isWatched)
        {
            name.SetTextColor(PaperTheme.InkMuted);
        }

        line.AddView(name, new LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (isWatched)
        {
            var size = PaperTheme.Dp(context, DesignTokens.Space.X4);
            line.AddView(new TickView(context), new LayoutParams(size, size));
        }

        column.AddView(line);

        // The official synopsis, opened in place by a tap (D217). Never a still (D37).
        var synopsis = PaperUi.Secondary(context, episode.Synopsis.Trim());
        synopsis.SetTextColor(PaperTheme.InkSoft);
        synopsis.SetPadding(PaperTheme.Dp(context, DesignTokens.Space.X6), PaperTheme.Dp(context, DesignTokens.Space.X1), 0, 0);
        synopsis.Visibility = ViewStates.Gone;
        column.AddView(synopsis);

        column.ContentDescription = $"Episode {episode.Episode.Number}, {name.Text}{(isWatched ? ", watched" : string.Empty)}{(isNext ? ", next" : string.Empty)}";
        column.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(episode.Synopsis))
            {
                synopsis.Visibility = synopsis.Visibility == ViewStates.Visible ? ViewStates.Gone : ViewStates.Visible;
            }
        };
        column.LongClick += (_, args) =>
        {
            onLongPress(episode);
            args.Handled = true;
        };
        return column;
    }

    private static Color Identity => Color.ParseColor(DesignTokens.Identity.Pulp.Mark);

    private static View Rule(Context context, Color colour)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(colour);
        rule.LayoutParameters = new LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    /// <summary>The pine tick a watched episode takes: the Action role's mark, drawn rather than shipped.</summary>
    private sealed class TickView : View
    {
        private readonly Paint paint = new() { AntiAlias = true };

        public TickView(Context context) : base(context)
        {
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = PaperTheme.Dp(context, 2);
            paint.StrokeCap = Paint.Cap.Round;
            paint.StrokeJoin = Paint.Join.Round;
            paint.Color = PaperTheme.Accent;
            ContentDescription = "Watched";
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            using var path = new Path();
            path.MoveTo(Width * 0.22f, Height * 0.53f);
            path.LineTo(Width * 0.41f, Height * 0.72f);
            path.LineTo(Width * 0.78f, Height * 0.28f);
            canvas.DrawPath(path, paint);
        }
    }

    /// <summary>The fold's chevron in the muted ink: up when the season is open, down when it is closed.</summary>
    private sealed class ChevronView : View
    {
        private readonly Paint paint = new() { AntiAlias = true };
        private readonly bool up;

        public ChevronView(Context context, bool up) : base(context)
        {
            this.up = up;
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = PaperTheme.Dp(context, 2);
            paint.StrokeCap = Paint.Cap.Round;
            paint.Color = PaperTheme.InkMuted;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            var (from, to) = up ? (0.62f, 0.4f) : (0.4f, 0.62f);
            using var path = new Path();
            path.MoveTo(Width * 0.28f, Height * from);
            path.LineTo(Width * 0.5f, Height * to);
            path.LineTo(Width * 0.72f, Height * from);
            canvas.DrawPath(path, paint);
        }
    }
}
