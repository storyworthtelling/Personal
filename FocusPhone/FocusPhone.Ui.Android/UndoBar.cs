using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;

namespace FocusPhone.Ui;

/// <summary>
/// The undo row (DESIGN.md §7): the action has already happened; this bar
/// names it and offers a 6-second window to reverse it. Never a dialog.
/// Overlay it on a FrameLayout host, bottom-aligned.
/// </summary>
public static class UndoBar
{
    public static void Show(Context context, FrameLayout host, string message, Action onUndo)
    {
        // One bar at a time: a new action replaces any bar still showing.
        Dismiss(host);

        var bar = new LinearLayout(context)
        {
            Orientation = Orientation.Horizontal,
            Tag = HostTag,
        };
        bar.SetGravity(GravityFlags.CenterVertical);
        bar.Background = PaperUi.Outline(context, PaperTheme.PaperDeep, PaperTheme.Hairline, DesignTokens.Shape.RadiusCard);
        var pad = PaperTheme.Dp(context, DesignTokens.Space.X4);
        bar.SetPadding(pad, PaperTheme.Dp(context, DesignTokens.Space.X2), pad, PaperTheme.Dp(context, DesignTokens.Space.X2));

        var text = PaperUi.Secondary(context, message);
        text.SetTextColor(PaperTheme.Ink);
        bar.AddView(text, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent) { Weight = 1 });

        var handler = new Handler(Looper.MainLooper!);
        var undo = PaperUi.QuietButton(context, "Undo", () =>
        {
            handler.RemoveCallbacksAndMessages(null);
            host.RemoveView(host.FindViewWithTag(HostTag));
            onUndo();
        });
        bar.AddView(undo);

        var layoutParams = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom);
        var margin = PaperTheme.Dp(context, DesignTokens.Space.X4);
        layoutParams.SetMargins(margin, 0, margin, margin);
        bar.Alpha = 0f;
        host.AddView(bar, layoutParams);
        bar.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();

        handler.PostDelayed(() =>
        {
            var current = host.FindViewWithTag(HostTag);
            if (current is not null)
            {
                current.Animate()!.Alpha(0f).SetDuration(DesignTokens.Motion.QuickMs)
                    .WithEndAction(new Java.Lang.Runnable(() => host.RemoveView(current)))!.Start();
            }
        }, DesignTokens.Motion.UndoWindowMs);
    }

    private static void Dismiss(FrameLayout host)
    {
        var existing = host.FindViewWithTag(HostTag);
        if (existing is not null)
        {
            host.RemoveView(existing);
        }
    }

    private static readonly Java.Lang.String HostTag = new("paper-undo-bar");
}
