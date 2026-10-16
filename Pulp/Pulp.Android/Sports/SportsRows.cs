using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Sports;
using Pulp.Library;

namespace Pulp.Sports;

/// <summary>
/// The Sports screen's pieces, painted from <see cref="SportsScreenPlan"/> rows (mockup 07): a league's
/// line, a team's, a game's, the championship, the Spoilers tag and the chip. Every word arrives decided.
/// </summary>
internal static class SportsRows
{
    private static int Dp(Activity host, int dp) => PaperTheme.Dp(host, dp);

    public static View Badge(Activity host, string name, string artUrl, bool shield, int letters, Action<SportsBadgeView, string> loadArt)
    {
        var badge = new SportsBadgeView(host, SportsScreenText.Monogram(name, letters), shield);
        if (artUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            loadArt(badge, artUrl);
        }

        badge.LayoutParameters = new LinearLayout.LayoutParams(Dp(host, SportsBadgeView.WidthDp), Dp(host, SportsBadgeView.HeightDp(shield)))
        {
            MarginEnd = Dp(host, DesignTokens.Space.X2),
        };
        return badge;
    }

    /// <summary>The Spoilers tag (D242): the one thing a hold draws. Its hit area is a full touch target around the small pill.</summary>
    public static View SpoilersTag(Activity host, Action onTap)
    {
        var pill = PaperUi.Label(host, "Spoilers");
        pill.SetTextColor(PaperTheme.PaperBright);
        pill.Background = PaperUi.Fill(host, PaperTheme.InkBlue, DesignTokens.Shape.RadiusInput);
        var padX = Dp(host, DesignTokens.Space.X2);
        pill.SetPadding(padX, Dp(host, DesignTokens.Space.X1) / 2, padX, Dp(host, DesignTokens.Space.X1) / 2);

        var hit = new FrameLayout(host) { Clickable = true };
        hit.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
        hit.AddView(pill, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center));
        hit.Click += (_, _) => onTap();
        return hit;
    }

    /// <summary>A chip on a game or the championship: hairline outline, ink for the ones that matter (Playoffs, Final).</summary>
    public static View Chip(Activity host, string text)
    {
        var chip = PaperUi.Label(host, text);
        var strong = text is "Playoffs" or "Final";
        chip.SetTextColor(strong ? PaperTheme.Ink : PaperTheme.InkMuted);
        chip.Background = PaperUi.Outline(host, PaperTheme.Paper, strong ? PaperTheme.Ink : PaperTheme.Hairline, DesignTokens.Shape.RadiusInput);
        var padX = Dp(host, DesignTokens.Space.X2);
        chip.SetPadding(padX, 0, padX, 0);
        return chip;
    }

    public static View LeagueLine(Activity host, LeaguePlan league, View badge, Action onToggle, Action onTag)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
        line.AddView(badge);

        var name = PaperUi.Heading(host, league.Name);
        if (!league.Open && !league.SpoilersTag && league.LineNote is not null)
        {
            name.SetTextColor(PaperTheme.Ink);
        }

        line.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        if (league.SpoilersTag)
        {
            line.AddView(SpoilersTag(host, onTag));
        }
        else if (league.LineNote is { } note)
        {
            var state = PaperUi.Secondary(host, note);
            state.Gravity = GravityFlags.End;
            line.AddView(state, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = Dp(host, DesignTokens.Space.X2),
            });
        }

        line.AddView(new SportsChevronView(host, league.Open), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginStart = Dp(host, DesignTokens.Space.X2),
        });
        line.Click += (_, _) => onToggle();
        return line;
    }

    public static View TeamLine(Activity host, TeamPlan team, View badge, Action onToggle, Action onHold)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
        line.AddView(badge);
        var name = PaperUi.Body(host, team.Name);
        name.SetTypeface(PaperTheme.SansSemibold(host), TypefaceStyle.Normal);
        line.AddView(name, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        line.AddView(new SportsChevronView(host, team.Open));
        line.Click += (_, _) => onToggle();
        line.LongClick += (_, args) =>
        {
            args.Handled = true;
            onHold();
        };
        return line;
    }

    public static View GameRow(Activity host, GameRow game, Action onPlay, Action onHold, Action onHighlights)
    {
        var row = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        var pad = Dp(host, DesignTokens.Space.X2);
        row.SetPadding(0, pad, 0, pad);
        row.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));

        var lines = LibraryRows.Lines(host);
        lines.AddView(PaperUi.Body(host, game.Title));
        var detail = PaperUi.Secondary(host, game.Detail);
        detail.SetTextColor(game.Recorded ? PaperTheme.Ink : PaperTheme.InkSoft);
        lines.AddView(detail);
        if (game.ChannelLine is { } channel)
        {
            lines.AddView(PaperUi.Secondary(host, channel));
        }

        if (game.HighlightsOffered)
        {
            // A quiet line, drawn from the generic label only (D313): nothing of a video is fetched until it is tapped.
            var peek = PaperUi.Secondary(host, "Highlights");
            peek.SetTextColor(PaperTheme.InkBlue);
            peek.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
            peek.Gravity = GravityFlags.CenterVertical;
            peek.Clickable = true;
            peek.Click += (_, _) => onHighlights();
            lines.AddView(peek);
        }

        row.AddView(lines, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        if (game.Chip is { } chip)
        {
            row.AddView(Chip(host, chip), new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent)
            {
                MarginStart = Dp(host, DesignTokens.Space.X2),
            });
        }

        row.Click += (_, _) => onPlay();
        row.LongClick += (_, args) =>
        {
            args.Handled = true;
            onHold();
        };
        return row;
    }

    /// <summary>The quiet "Earlier" line over a team's watched, finished games (D330): secondary ink, a chevron, a full touch target.</summary>
    public static View EarlierLine(Activity host, string label, bool open, Action onToggle)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
        line.AddView(PaperUi.Secondary(host, label), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        line.AddView(new SportsChevronView(host, open));
        line.Click += (_, _) => onToggle();
        return line;
    }

    /// <summary>The championship at league level (D239): its name in Serif, its date, the chip, the finalists once known.</summary>
    public static View Championship(Activity host, ChampionshipRow champ)
    {
        var row = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        var pad = Dp(host, DesignTokens.Space.X2);
        row.SetPadding(0, pad, 0, pad);
        var lines = LibraryRows.Lines(host);
        lines.AddView(PaperUi.Heading(host, champ.Name));
        lines.AddView(PaperUi.Secondary(host, champ.Detail));
        if (champ.TeamsLine is { } teams)
        {
            lines.AddView(PaperUi.Secondary(host, teams));
        }

        row.AddView(lines, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        row.AddView(Chip(host, champ.Chip));
        return row;
    }

    /// <summary>A link line behind an open league: its label, and a chevron.</summary>
    public static View LinkLine(Activity host, string label, Action onTap)
    {
        var line = new LinearLayout(host) { Orientation = Orientation.Horizontal, Clickable = true };
        line.SetGravity(GravityFlags.CenterVertical);
        line.SetMinimumHeight(Dp(host, DesignTokens.Shape.TouchTargetDp));
        var text = PaperUi.Body(host, label);
        text.SetTypeface(PaperTheme.SansSemibold(host), TypefaceStyle.Normal);
        line.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        line.AddView(PaperUi.Body(host, "›"));
        line.Click += (_, _) => onTap();
        return line;
    }

    /// <summary>A team's block: a hairline rule down its left edge, as mockup 07 indents it.</summary>
    public static LinearLayout TeamBlock(Activity host)
    {
        var block = new LinearLayout(host) { Orientation = Orientation.Vertical };
        var rule = new GradientDrawableHolder(host).Drawable;
        block.Background = rule;
        block.SetPadding(Dp(host, DesignTokens.Space.X3), 0, 0, 0);
        block.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            MarginStart = Dp(host, DesignTokens.Space.X2),
        };
        return block;
    }

    public static View Rule(Activity host, bool strong)
    {
        var rule = new View(host);
        rule.SetBackgroundColor(strong ? PaperTheme.Ink : PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(host, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    /// <summary>A left-edge rule built as a layer: hairline-coloured strip, paper over the rest.</summary>
    private sealed class GradientDrawableHolder
    {
        public GradientDrawableHolder(Activity host)
        {
            var strip = new Android.Graphics.Drawables.ColorDrawable(PaperTheme.Hairline);
            var ground = new Android.Graphics.Drawables.ColorDrawable(Android.Graphics.Color.Transparent);
            var layers = new Android.Graphics.Drawables.LayerDrawable([ground, strip]);
            layers.SetLayerGravity(1, GravityFlags.Start);
            layers.SetLayerWidth(1, Dp(host, DesignTokens.Space.X1) / 2);
            Drawable = layers;
        }

        public Android.Graphics.Drawables.Drawable Drawable { get; }
    }
}
