using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.App;
using Pulp.Domain.Library;

namespace Pulp.Library;

/// <summary>
/// The pieces mockups 04 and 05 share: a poster window beside a column of lines, a hairline under
/// each row, the pencil progress mark, and the text styles the rows use. Every colour and size is a
/// token through <see cref="PaperTheme"/> and <see cref="PaperUi"/>.
/// </summary>
internal static class LibraryRows
{
    /// <summary>A playlist row's poster (mockup 04: 40 × 60).</summary>
    public const int PlaylistPosterWidthDp = 40;

    /// <summary>A Recently watched row's poster is a little smaller (mockup 05: 36 × 54).</summary>
    public const int RecentPosterWidthDp = 36;

    /// <summary>Posters are 2:3, as the catalogue's vertical posters are.</summary>
    public static int PosterHeightDp(int widthDp) => widthDp * 3 / 2;

    /// <summary>A struck row's poster sits back a little (mockup 04, D101).</summary>
    public const float StruckPosterAlpha = 0.6f;

    /// <summary>The pencil mark's thickness (mockup 05: 3px).</summary>
    private const int MarkThicknessDp = 3;

    /// <summary>A row: optional lead view (the grip), the poster, then the column of lines. The hairline sits under it.</summary>
    public static LinearLayout Row(Activity host, View? lead, ArtworkWindow poster, int posterWidthDp, LinearLayout lines, int padDp)
    {
        var row = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        var pad = PaperTheme.Dp(host, padDp);
        row.SetPadding(0, pad, 0, pad);
        row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.ListRowMinHeightDp));

        if (lead is not null)
        {
            row.AddView(lead, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.MatchParent));
        }

        row.AddView(poster, new LinearLayout.LayoutParams(
            PaperTheme.Dp(host, posterWidthDp), PaperTheme.Dp(host, PosterHeightDp(posterWidthDp)))
        {
            MarginEnd = PaperTheme.Dp(host, DesignTokens.Space.X3),
        });

        row.AddView(lines, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });
        return row;
    }

    public static View Hairline(Activity host)
    {
        var rule = new View(host);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(host, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    public static LinearLayout Lines(Activity host) => new(host) { Orientation = Orientation.Vertical };

    /// <summary>The title line: Sans at body size, in ink.</summary>
    public static TextView TitleLine(Activity host, string text) => PaperUi.Body(host, text);

    /// <summary>The kind line ("Film · 1h 44m"): secondary size in the softer ink.</summary>
    public static TextView KindLine(Activity host, string text)
    {
        var view = PaperUi.Secondary(host, text);
        view.SetTextColor(PaperTheme.InkSoft);
        return view;
    }

    /// <summary>The where line: the quietest, unless it carries "not on your services".</summary>
    public static TextView WhereLine(Activity host, string text, bool emphasis)
    {
        var view = PaperUi.Secondary(host, text);
        view.SetTextColor(emphasis ? PaperTheme.InkSoft : PaperTheme.InkMuted);
        return view;
    }

    /// <summary>Strikes a title through in ink and quietens it (D101, mockup 04).</summary>
    public static void Strike(TextView title)
    {
        title.PaintFlags |= PaintFlags.StrikeThruText;
        title.SetTextColor(PaperTheme.InkMuted);
    }

    /// <summary>The pencil progress mark across a whole show (mockup 05, D227): the identity's mark on a deep-paper track.</summary>
    public static View ProgressMark(Activity host, ProgressMark mark)
    {
        var track = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        track.Background = PaperUi.Fill(host, PaperTheme.PaperDeep, DesignTokens.Shape.HairlineDp);
        var done = new View(host)
        {
            Background = PaperUi.Fill(host, Color.ParseColor(DesignTokens.Identity.Pulp.Mark), DesignTokens.Shape.HairlineDp),
        };
        var at = Math.Clamp(mark.At, 0, Math.Max(mark.Of, 1));
        track.AddView(done, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent) { Weight = at });
        track.AddView(new View(host), new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent) { Weight = Math.Max(mark.Of, 1) - at });
        track.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(host, MarkThicknessDp))
        {
            TopMargin = PaperTheme.Dp(host, DesignTokens.Space.X1),
            BottomMargin = PaperTheme.Dp(host, DesignTokens.Space.X1),
        };
        return track;
    }

    /// <summary>A poster window that fills in once its poster arrives; the bookplate shows until then.</summary>
    public static ArtworkWindow Poster(Activity host, string? url, int widthDp, CancellationToken cancellationToken)
    {
        var window = new ArtworkWindow(host);
        if (string.IsNullOrWhiteSpace(url))
        {
            return window;
        }

        var target = PaperTheme.Dp(host, PosterHeightDp(widthDp));
        _ = FillAsync(host, window, url, target, cancellationToken);
        return window;
    }

    private static async Task FillAsync(Activity host, ArtworkWindow window, string url, int targetPx, CancellationToken cancellationToken)
    {
        try
        {
            var bitmap = await Task.Run(() => PulpServices.For(host).Posters.LoadAsync(url, targetPx, cancellationToken), cancellationToken);
            if (bitmap is not null && !host.IsFinishing && !host.IsDestroyed)
            {
                host.RunOnUiThread(() => window.SetArtwork(bitmap));
            }
        }
        catch (System.OperationCanceledException)
        {
            // The page went away before the poster arrived; the window went with it.
            return;
        }
        catch (Exception exception)
        {
            PulpServices.For(host).Diagnostics.Warn("posters", "A poster could not be shown; the row keeps its bookplate.", exception);
        }
    }
}
