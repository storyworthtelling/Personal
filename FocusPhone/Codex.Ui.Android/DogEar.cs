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
///
/// <para><b>The fold has two jobs, and never both at once (§13).</b> As a <b>notice</b> it marks something
/// waiting — a failed send, a broken source, a lost role — and you turn to it when ready, with no clock
/// running. As an <b>undo</b> it carries the back-turn arrow and reverses the act, and it holds only for the
/// undo window. An undo therefore <b>overrides</b> any notice fold for its window and the notices come back
/// when it closes, so the corner never means two things at once.</para>
///
/// <para>Nothing else in the app announces itself: errors live here rather than as banners or cards on the
/// page, and the words are always static-neutral — a wry line delivered on the far side of a fold would
/// arrive as a reward for turning the corner.</para>
/// </summary>
public static class DogEar
{
    /// <summary>
    /// One thing waiting behind the fold. <paramref name="Hue"/> is the functional colour it carries;
    /// an action is optional, because most notices are things to know rather than things to do.
    /// </summary>
    public sealed record Notice(
        string Headline, string Detail, Color Hue, string? ActionLabel = null, Action? OnAction = null);

    // What is waiting, per host. Held off to the side rather than on the view because the
    // notice fold is removed and rebuilt whenever an undo takes the corner, and the list has
    // to outlive that.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameLayout, NoticeBox> Waiting = new();

    private sealed class NoticeBox
    {
        public IReadOnlyList<Notice> Notices { get; set; } = [];
    }

    /// <summary>
    /// Sets what is waiting behind the fold, newest first. An empty list flattens the corner.
    /// Safe to call on every render: it replaces rather than accumulates, so a screen can simply
    /// state what is wrong each time it draws.
    /// </summary>
    public static void ShowNotices(Context context, FrameLayout host, IReadOnlyList<Notice> notices)
    {
        Waiting.GetOrCreateValue(host).Notices = notices;

        // An undo is a closing window and outranks anything that merely waits, so it keeps the
        // corner; the notices reappear the moment it is gone.
        if (host.FindViewWithTag(HostTag) is not null)
        {
            return;
        }

        RepaintNotices(context, host);
    }

    /// <summary>Flattens the notice fold and forgets what was waiting.</summary>
    public static void ClearNotices(FrameLayout host)
    {
        Waiting.GetOrCreateValue(host).Notices = [];
        if (host.FindViewWithTag(NoticeTag) is View existing)
        {
            host.RemoveView(existing);
        }
    }

    private static void RepaintNotices(Context context, FrameLayout host)
    {
        if (host.FindViewWithTag(NoticeTag) is View existing)
        {
            host.RemoveView(existing);
        }

        var notices = Waiting.GetOrCreateValue(host).Notices;
        if (notices.Count == 0)
        {
            return;
        }

        // One fold, no count (§13): the corner says something waits, not how much.
        var size = PaperTheme.Dp(context, 72);
        var fold = new FoldView(context, notices[0].Headline, timer: null, showsBackArrow: false, hue: notices[0].Hue)
        {
            Tag = NoticeTag,
        };
        fold.Click += (_, _) => OpenNotices(context, host);

        fold.Alpha = 0f;
        host.AddView(fold, new FrameLayout.LayoutParams(size, size, GravityFlags.Top | GravityFlags.End));
        fold.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
    }

    private static void OpenNotices(Context context, FrameLayout host)
    {
        var notices = Waiting.GetOrCreateValue(host).Notices;
        if (notices.Count == 0)
        {
            return;
        }

        var lines = notices
            .Select(notice => new CardAction(
                notice.Headline,
                () => notice.OnAction?.Invoke(),
                Detail: notice.ActionLabel is null ? notice.Detail : $"{notice.Detail}\n{notice.ActionLabel}",
                Hue: notice.Hue))
            .ToArray();

        ActionCard.Show(context, host, "Waiting", subtitle: null, lines);
    }

    private static readonly Java.Lang.String NoticeTag = new("paper-dog-ear-notice");

    public static void Show(Context context, FrameLayout host, string undoHint, Action onUndo)
    {
        Dismiss(host);

        // The undo takes the corner outright for its window; whatever was waiting comes
        // back when it closes, so the fold never means two things at once (§13).
        if (host.FindViewWithTag(NoticeTag) is View notice)
        {
            host.RemoveView(notice);
        }

        var size = PaperTheme.Dp(context, 72);
        var handler = new Handler(Looper.MainLooper!);
        var fold = new FoldView(context, undoHint, handler, showsBackArrow: true, hue: PaperTheme.Accent)
        {
            Tag = HostTag,
        };

        fold.Click += (_, _) =>
        {
            handler.RemoveCallbacksAndMessages(null);
            host.RemoveView(host.FindViewWithTag(HostTag));
            onUndo();
            RepaintNotices(context, host);
        };

        var layoutParams = new FrameLayout.LayoutParams(size, size, GravityFlags.Top | GravityFlags.End);
        fold.Alpha = 0f;
        host.AddView(fold, layoutParams);
        fold.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();

        handler.PostDelayed(() =>
        {
            var current = host.FindViewWithTag(HostTag);
            current?.Animate()!.Alpha(0f).SetDuration(DesignTokens.Motion.QuickMs)
                .WithEndAction(new Java.Lang.Runnable(() =>
                {
                    host.RemoveView(current);
                    RepaintNotices(context, host);
                }))!.Start();
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
        if (existing is FoldView { Timer: { } outgoing })
        {
            outgoing.RemoveCallbacksAndMessages(null);
        }
        host.RemoveView(existing);
    }

    private static readonly Java.Lang.String HostTag = new("paper-dog-ear");

    // Draws a folded top-right page corner: the page curls to reveal a coloured underside with a soft fold
    // shadow. The undo form adds a back-turn arrow; a notice form deliberately does not, because it turns
    // TO something rather than reversing anything.
    private sealed class FoldView : View
    {
        private readonly float density;
        private readonly bool showsBackArrow;
        private readonly Color hue;

        public FoldView(Context context, string hint, Handler? timer, bool showsBackArrow, Color hue)
            : base(context)
        {
            density = context.Resources!.DisplayMetrics!.Density;
            Timer = timer;
            this.showsBackArrow = showsBackArrow;
            this.hue = hue;
            Clickable = true;
            ContentDescription = hint;
        }

        /// <summary>The auto-flatten timer, held so a replacing fold can cancel it. Null for a notice,
        /// which has no clock — it waits until it is turned to.</summary>
        public Handler? Timer { get; }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            float w = Width, h = Height;

            // The triangular fold (top-right).
            using var underside = new Paint { AntiAlias = true, Color = hue };
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

            if (!showsBackArrow)
            {
                return;
            }

            // A small back-turn arrow (↶), and ONLY on the undo fold: it is what says this
            // one reverses the act rather than turning to something waiting.
            using var mark = new Paint(PaintFlags.AntiAlias) { Color = PaperTheme.Paper, TextAlign = Paint.Align.Center };
            mark.SetTypeface(PaperTheme.SerifSemibold(Context!));
            mark.TextSize = 18 * density;
            canvas.DrawText("↶", w * 0.66f, h * 0.42f, mark);
        }
    }
}
