using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;

// Both System.IO and Android.Graphics define Path, and ImplicitUsings brings the
// former in — so the bare name is ambiguous. Drawing code means the graphics one.
using Path = Android.Graphics.Path;

namespace Codex.Ui;

/// <summary>
/// The book-metaphor undo affordance (DESIGN.md §13): instead of a floating bar that reads like a toast,
/// a destructive act folds a <b>dog-ear</b> into the top-right corner of the page. Tapping the fold turns
/// back — it reverses the act. The corner auto-flattens (dismisses) after the undo window. Overlay it on a
/// FrameLayout host. One at a time; a new act replaces any fold still showing.
/// </summary>
public static class DogEar
{
    public static void Show(Context context, FrameLayout host, string undoHint, Action onUndo)
    {
        Dismiss(host);

        var size = PaperTheme.Dp(context, 72);
        var fold = new FoldView(context, undoHint) { Tag = HostTag };

        var handler = new Handler(Looper.MainLooper!);
        fold.Click += (_, _) =>
        {
            handler.RemoveCallbacksAndMessages(null);
            host.RemoveView(host.FindViewWithTag(HostTag));
            onUndo();
        };

        var layoutParams = new FrameLayout.LayoutParams(size, size, GravityFlags.Top | GravityFlags.End);
        fold.Alpha = 0f;
        host.AddView(fold, layoutParams);
        fold.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();

        handler.PostDelayed(() =>
        {
            var current = host.FindViewWithTag(HostTag);
            current?.Animate()!.Alpha(0f).SetDuration(DesignTokens.Motion.QuickMs)
                .WithEndAction(new Java.Lang.Runnable(() => host.RemoveView(current)))!.Start();
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

    private static readonly Java.Lang.String HostTag = new("paper-dog-ear");

    // Draws a folded top-right page corner: the page curls to reveal a warm underside with a soft fold
    // shadow, and a small back-turn arrow to signal "tap to undo".
    private sealed class FoldView : View
    {
        private readonly float density;
        private readonly string hint;

        public FoldView(Context context, string hint) : base(context)
        {
            density = context.Resources!.DisplayMetrics!.Density;
            this.hint = hint;
            Clickable = true;
            ContentDescription = hint;
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float w = Width, h = Height;

            // The triangular fold (top-right).
            using var underside = new Paint { AntiAlias = true, Color = PaperTheme.Accent };
            using var tri = new Path();
            tri.MoveTo(w, 0);
            tri.LineTo(0, 0);
            tri.LineTo(w, h);
            tri.Close();
            canvas.DrawPath(tri, underside);

            // A soft diagonal shadow along the fold line for depth.
            using var shadow = new Paint { AntiAlias = true, StrokeWidth = 2 * density };
            shadow.SetStyle(Paint.Style.Stroke);
            shadow.Color = new Color(PaperTheme.Ink.R, PaperTheme.Ink.G, PaperTheme.Ink.B, (byte)60);
            canvas.DrawLine(0, 0, w, h, shadow);

            // A small back-turn arrow (↶) on the fold to read as "undo".
            using var mark = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.Paper, TextAlign = Paint.Align.Center };
            mark.SetTypeface(PaperTheme.SerifSemibold(Context!));
            mark.TextSize = 18 * density;
            canvas.DrawText("↶", w * 0.66f, h * 0.42f, mark);
        }
    }
}
