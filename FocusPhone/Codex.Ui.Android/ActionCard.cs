using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

namespace Codex.Ui;

/// <summary>
/// One line on the card. Lead marks the primary intent; Destructive warns. Detail is a
/// second, quieter line beneath the label — a notice needs to say what happened, not just
/// name itself, and Hue lets a notice carry its own functional colour (§13).
/// </summary>
public sealed record CardAction(
    string Label,
    Action Invoke,
    bool Lead = false,
    bool Destructive = false,
    string? Detail = null,
    Color? Hue = null);

/// <summary>
/// Item actions (DESIGN.md §7): long-press opens a <b>centred paper card</b> over a
/// PaperDeep haze; tapping outside dismisses it. This is the only overflow the app
/// has — §9 bans the kebab menu — and it is <b>never a confirm dialog</b>: the
/// entries here do things, and anything destructive happens immediately with an
/// undo dog-ear rather than asking first.
///
/// Overlay it on a FrameLayout host, like the dog-ear and the undo bar.
/// </summary>
public static class ActionCard
{
    public static void Show(
        Context context, FrameLayout host, string title, string? subtitle, params CardAction[] actions)
    {
        Dismiss(host);

        // The haze: a dimmed PaperDeep, not a black scrim. Paper does not go dark.
        var scrim = new FrameLayout(context) { Tag = HostTag };
        scrim.SetBackgroundColor(new Color(
            PaperTheme.PaperDeep.R, PaperTheme.PaperDeep.G, PaperTheme.PaperDeep.B, HazeAlpha));
        scrim.Click += (_, _) => Dismiss(host);

        var card = new LinearLayout(context) { Orientation = Orientation.Vertical };
        card.Background = PaperUi.Outline(
            context, PaperTheme.PaperBright, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        // Swallow taps on the card so they do not fall through and dismiss it.
        card.Click += (_, _) => { };

        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        card.SetPadding(0, pad, 0, PaperTheme.Dp(context, DesignTokens.Space.X2));

        AddHeader(context, card, title, subtitle, pad);

        foreach (var action in actions)
        {
            card.AddView(BuildRow(context, host, action, pad));
        }

        var edge = PaperTheme.Dp(context, DesignTokens.Shape.ScreenEdgeDp);
        var layout = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Center);
        layout.SetMargins(edge, 0, edge, 0);
        scrim.AddView(card, layout);

        scrim.Alpha = 0f;
        host.AddView(scrim, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        scrim.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
    }

    private static void AddHeader(
        Context context, LinearLayout card, string title, string? subtitle, int pad)
    {
        var header = new LinearLayout(context) { Orientation = Orientation.Vertical };
        header.SetPadding(pad, 0, pad, PaperTheme.Dp(context, DesignTokens.Space.X3));

        var heading = PaperUi.Heading(context, title);
        heading.SetSingleLine(true);
        heading.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
        header.AddView(heading);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            var secondary = PaperUi.Secondary(context, subtitle);
            secondary.SetSingleLine(true);
            secondary.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            header.AddView(secondary);
        }

        card.AddView(header);
        card.AddView(Hairline(context));
    }

    private static View BuildRow(Context context, FrameLayout host, CardAction action, int pad)
    {
        var label = PaperUi.Body(context, action.Label);
        label.SetTextColor(action.Hue ?? (action.Destructive
            ? PaperTheme.ProblemMark
            : action.Lead ? PaperTheme.ActionText : PaperTheme.Ink));

        if (action.Lead || action.Destructive || action.Hue is not null)
        {
            label.SetTypeface(PaperTheme.SansSemibold(context), TypefaceStyle.Normal);
        }

        var vertical = PaperTheme.Dp(context, DesignTokens.Space.X3);
        var row = new LinearLayout(context) { Orientation = Orientation.Vertical };
        row.SetPadding(pad, vertical, pad, vertical);
        row.SetMinimumHeight(PaperTheme.Dp(context, DesignTokens.Shape.TouchTargetDp));
        row.AddView(label);

        // A notice has to say what happened, not just name itself. An action with nothing
        // to add stays a single line, so the card is unchanged everywhere it was before.
        if (!string.IsNullOrWhiteSpace(action.Detail))
        {
            row.AddView(PaperUi.Secondary(context, action.Detail));
        }

        row.Clickable = true;
        row.Click += (_, _) =>
        {
            Dismiss(host);
            action.Invoke();
        };

        return row;
    }

    private static View Hairline(Context context)
    {
        var rule = new View(context);
        rule.SetBackgroundColor(PaperTheme.Hairline);
        rule.LayoutParameters = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, PaperTheme.Dp(context, DesignTokens.Shape.HairlineDp));
        return rule;
    }

    public static void Dismiss(FrameLayout host)
    {
        var existing = host.FindViewWithTag(HostTag);
        if (existing is not null)
        {
            host.RemoveView(existing);
        }
    }

    // Enough to settle the page back without hiding it — you are still looking at
    // the row you long-pressed, which is the point of a card over a dialog.
    private const byte HazeAlpha = 219;

    private static readonly Java.Lang.String HostTag = new("paper-action-card");
}
