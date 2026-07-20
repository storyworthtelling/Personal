using Android.Content;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;

namespace FocusPhone.Ui;

/// <summary>
/// The shared now-playing bar pinned above the bottom bar: current title plus
/// play/pause and 30s skip controls. Hidden when nothing is loaded. The owner
/// updates it from a poll; it never observes the player itself.
/// </summary>
public sealed class NowPlayingBar : LinearLayout
{
    private readonly TextView title;
    private readonly Button playPause;

    public NowPlayingBar(Context context, Action onBack, Action onPlayPause, Action onForward) : base(context)
    {
        Orientation = Orientation.Vertical;

        var rule = new Android.Views.View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        AddView(rule, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp)));

        var row = new LinearLayout(context) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        var horizontal = PaperTheme.Dp(context, DesignTokens.Space.X4);
        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X2);
        row.SetPadding(horizontal, vertical, horizontal, vertical);

        title = new TextView(context)
        {
            TextSize = DesignTokens.TypeSp.Secondary,
            Typeface = PaperTheme.SansSemibold(context),
            Ellipsize = Android.Text.TextUtils.TruncateAt.End,
        };
        title.SetTextColor(PaperTheme.Ink);
        title.SetSingleLine(true);
        row.AddView(title, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        row.AddView(PaperUi.QuietButton(context, "−30s", onBack));
        playPause = PaperUi.QuietButton(context, "Play", onPlayPause);
        row.AddView(playPause);
        row.AddView(PaperUi.QuietButton(context, "+30s", onForward));

        AddView(row, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        Visibility = ViewStates.Gone;
    }

    public void Update(string? titleText, bool playing)
    {
        if (string.IsNullOrEmpty(titleText))
        {
            Visibility = ViewStates.Gone;
            return;
        }

        Visibility = ViewStates.Visible;
        title.Text = titleText;
        playPause.Text = playing ? "Pause" : "Play";
    }
}
