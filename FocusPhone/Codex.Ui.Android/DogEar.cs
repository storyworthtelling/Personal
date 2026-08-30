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
/// The book-metaphor undo affordance (DESIGN.md §7 and §13): instead of a floating bar that reads like a
/// toast, a destructive act folds a <b>dog-ear</b> into the top-right corner of the page. Tapping the fold
/// turns back — it reverses the act. The corner auto-flattens (dismisses) after the undo window. Overlay it
/// on a FrameLayout host. One at a time; a new act replaces any fold still showing.
///
/// This is the suite's <b>only</b> undo affordance. It replaced a bottom UndoBar, which was the same idea in
/// the wrong dialect: two undo idioms in one design system, and the one that read as a toast was the one
/// most screens had reached for.
/// </summary>
public static class DogEar
{
    public static void Show(Context context, FrameLayout host, string undoHint, Action onUndo)
    {
        Dismiss(host);

        var size = PaperTheme.Dp(context, 72);
        var handler = new Handler(Looper.MainLooper!);
        var fold = new FoldView(context, undoHint, handler) { Tag = HostTag };

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
        if (host.FindViewWithTag(HostTag) is not View existing)
        {
            return;
        }

        // Cancel the OUTGOING fold's timer, not just its view. Each Show posts a
        // delayed fade that finds the current fold by tag, so a timer left running
        // from the fold being replaced would fade out its replacement early — the
        // second undo of a quick pair would get a fraction of its window.
        if (existing is FoldView outgoing)
        {
            outgoing.Timer.RemoveCallbacksAndMessages(null);
        }
        host.RemoveView(existing);
    }

    private static readonly Java.Lang.String HostTag = new("paper-dog-ear");

    // Draws a folded top-right page corner: the page curls to reveal a warm underside with a soft fold
    // shadow, and a small back-turn arrow to signal "tap to undo".
    private sealed class FoldView : View
    {
        private readonly float density;

        public FoldView(Context context, string hint, Handler timer) : base(context)
        {
            density = context.Resources!.DisplayMetrics!.Density;
            Timer = timer;
            Clickable = true;
            ContentDescription = hint;
        }

        /// <summary>The auto-flatten timer, held so a replacing fold can cancel it.</summary>
        public Handler Timer { get; }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float w = Width, h = Height;

            // The triangular fold (top-right).
            using var underside = new Paint { AntiAlias = true, Color = PaperTheme.Accent };
            using var tri = new Android.Graphics.Path();
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
