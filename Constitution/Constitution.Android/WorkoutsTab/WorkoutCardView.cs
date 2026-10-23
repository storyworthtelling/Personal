using Android.App;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Constitution.Domain.Workouts.Screens;

namespace Constitution.WorkoutsTab;

/// <summary>
/// The card a long-press on a day raises (book p.12; DESIGN.md sections 7 and 8): a centred paper card over a PaperDeep haze
/// that covers the whole screen, binder included (so it sits in the window's content frame, not the page's). It is the
/// suite's action card composed for Constitution, because the shared one cannot turn into the days in place, grey a row
/// with its reason, or carry a day's mono label and a glyph. A row does its act and the card goes, and a
/// tap on the haze dismisses it. <see cref="ShowChoices"/> replaces the card's own contents: no second card, nothing to answer.
/// </summary>
internal sealed class WorkoutCardView
{
    private const byte HazeAlpha = 219;
    private const float DimmedRow = 0.45f;
    private const int BinderGutterDp = 66;
    private static readonly Java.Lang.String HostTag = new("constitution-workout-card");

    private readonly Activity host;
    private readonly Action onClosed;
    private readonly FrameLayout frame;
    private readonly FrameLayout scrim;
    private readonly LinearLayout card;

    public WorkoutCardView(Activity host, Action onClosed)
    {
        this.host = host;
        this.onClosed = onClosed;
        frame = host.FindViewById<FrameLayout>(Android.Resource.Id.Content)!;
        Remove(frame);

        scrim = new FrameLayout(host) { Tag = HostTag };
        scrim.SetBackgroundColor(new Color(PaperTheme.PaperDeep.R, PaperTheme.PaperDeep.G, PaperTheme.PaperDeep.B, HazeAlpha));
        scrim.Click += (_, _) => Dismiss();

        card = new LinearLayout(host) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(host, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        card.Click += (_, _) => { };

        var edge = PaperTheme.Dp(host, DesignTokens.Shape.ScreenEdgeDp);
        var layout = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center);
        layout.SetMargins(edge, 0, edge + PaperTheme.Dp(host, BinderGutterDp), 0);
        scrim.AddView(card, layout);
        scrim.Alpha = 0f;
        frame.AddView(scrim, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        scrim.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
    }

    /// <summary>Closes the card and tells the area, so the pressed clipping lets go.</summary>
    public void Dismiss()
    {
        Remove(frame);
        onClosed();
    }

    /// <summary>The card as the long-press raises it: its heading and exactly the actions the model gives.</summary>
    public void ShowActions(ActionCardModel model, Action<WorkoutActionId> chosen)
    {
        card.RemoveAllViews();
        AddHeader(model.Title, model.Subtitle);
        foreach (var action in model.Actions)
        {
            var row = Row();
            row.AddView(new ActionGlyphView(host, action.Id == WorkoutActionId.SkipThisDay), Spaced(DesignTokens.Space.X3));
            row.AddView(PaperUi.Body(host, action.Label));
            var id = action.Id;
            row.Click += (_, _) => chosen(id);
            card.AddView(row);
        }
    }

    /// <summary>The same card turned to the days to pick from, each greyed with its reason when it cannot be, none hidden.</summary>
    public void ShowChoices(ChoiceCard choices, Action<DateOnly> picked)
    {
        card.RemoveAllViews();
        AddHeader(choices.Title, choices.Subtitle);
        foreach (var choice in choices.Rows)
        {
            var row = Row();
            var label = PaperUi.Mono(host, choice.DayLabel);
            label.SetTextColor(PaperTheme.InkMuted);
            label.SetWidth(PaperTheme.Dp(host, DesignTokens.Space.X8));
            var text = PaperUi.Body(host, choice.Text);
            if (choice.IsEnabled)
            {
                row.AddView(label);
                row.AddView(text);
                var day = choice.Day;
                row.Click += (_, _) => picked(day);
            }
            else
            {
                label.Alpha = DimmedRow;
                text.Alpha = DimmedRow;
                var words = new LinearLayout(host) { Orientation = Orientation.Vertical };
                words.AddView(text);
                words.AddView(PaperUi.Secondary(host, choice.Reason!));
                row.AddView(label);
                row.AddView(words);
            }

            card.AddView(row);
        }
    }

    private void AddHeader(string title, string? subtitle)
    {
        var pad = PaperTheme.Dp(host, DesignTokens.Space.X4);
        var header = new LinearLayout(host) { Orientation = Orientation.Vertical };
        header.SetPadding(pad, pad, pad, PaperTheme.Dp(host, DesignTokens.Space.X3));
        var heading = PaperUi.Heading(host, title);
        heading.SetSingleLine(true);
        heading.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        header.AddView(heading);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            header.AddView(PaperUi.Secondary(host, subtitle));
        }

        card.AddView(header);
        card.AddView(Hairline());
    }

    private LinearLayout Row()
    {
        var pad = PaperTheme.Dp(host, DesignTokens.Space.X4);
        var row = new LinearLayout(host) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        row.SetPadding(pad, PaperTheme.Dp(host, DesignTokens.Space.X3), pad, PaperTheme.Dp(host, DesignTokens.Space.X3));
        row.SetMinimumHeight(PaperTheme.Dp(host, DesignTokens.Shape.TouchTargetDp));
        return row;
    }

    private LinearLayout.LayoutParams Spaced(int rightDp) =>
        new(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent) { RightMargin = PaperTheme.Dp(host, rightDp) };

    private View Hairline()
    {
        var rule = new View(host);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(host, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    private static void Remove(FrameLayout frame)
    {
        if (frame.FindViewWithTag(HostTag) is { } existing)
        {
            frame.RemoveView(existing);
        }
    }
}
