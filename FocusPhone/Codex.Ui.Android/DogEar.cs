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
        string Headline, string Detail, Color Hue, string? ActionLabel = null, Action? OnAction = null,
        DateTimeOffset? At = null);

    /// <summary>
    /// The colour of each functional role a notice can carry, by the same names as the MAUI kit's
    /// <c>DogEarHue</c> — Problem, Caution, Action, Resolved and, new with mockup 17's Waiting page,
    /// <b>Reference</b> (slate: only worth knowing) and <b>Progress</b> (teal: waiting, not wrong). The
    /// native fold takes a <see cref="Color"/> rather than a role, so these are what a caller reaches
    /// for instead of picking a <see cref="PaperTheme"/> field by hand; a guard keeps the two lists the
    /// same length and the same names.
    /// </summary>
    public static class Hues
    {
        public static Color Problem => PaperTheme.ProblemMark;
        public static Color Caution => PaperTheme.CautionMark;
        public static Color Action => PaperTheme.Accent;
        public static Color Resolved => PaperTheme.ResolvedMark;
        public static Color Reference => PaperTheme.ReferenceMark;
        public static Color Progress => PaperTheme.ProgressMark;
    }

    // What is waiting, per host. Held off to the side rather than on the view because the
    // notice fold is removed and rebuilt whenever an undo takes the corner, and the list has
    // to outlive that.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<FrameLayout, NoticeBox> Waiting = new();

    private sealed class NoticeBox
    {
        // Keyed. Several parts of a screen raise notices independently — the shell watches the SMS
        // role while the list watches the sync — and a plain replace would mean whichever spoke last
        // silently erased the other. The ORDER is NoticeOrder's (the MAUI kit's DogEarState uses a
        // byte-parallel copy): newest first by date, undated ones in a stable order, and a notice
        // stated again, unchanged, keeps its place.
        private readonly Dictionary<string, Notice> byKey = [];
        private readonly NoticeOrder order = new();

        public IReadOnlyList<Notice> Notices => order.Keys.Select(key => byKey[key]).ToList();

        public void Set(string key, Notice? notice)
        {
            if (notice is null)
            {
                byKey.Remove(key);
                order.Remove(key);
                return;
            }

            byKey[key] = notice;
            order.Set(key, notice.At, $"{notice.Headline}\u001f{notice.Detail}\u001f{notice.Hue}\u001f{notice.ActionLabel}");
        }

        public void Clear()
        {
            byKey.Clear();
            order.Clear();
        }
    }

    /// <summary>
    /// Sets or clears ONE owner's notice, leaving everyone else's alone. Passing null for
    /// <paramref name="notice"/> withdraws it. Safe to call on every render: an owner simply
    /// states what is wrong, or that nothing is, each time it draws.
    /// </summary>
    public static void SetNotice(Context context, FrameLayout host, string key, Notice? notice)
    {
        Waiting.GetOrCreateValue(host).Set(key, notice);

        if (host.FindViewWithTag(HostTag) is null)
        {
            RepaintNotices(context, host);
        }
    }

    /// <summary>
    /// Replaces every notice at once. For a screen that owns the whole corner; anything
    /// sharing it should use <see cref="SetNotice"/> instead.
    /// </summary>
    public static void ShowNotices(Context context, FrameLayout host, IReadOnlyList<Notice> notices)
    {
        var box = Waiting.GetOrCreateValue(host);
        box.Clear();

        // The list arrives in display order, and undated notices show most-recently-set first, so set
        // them last-to-first to keep the order the caller gave.
        for (var index = notices.Count - 1; index >= 0; index--)
        {
            box.Set($"anon-{index}", notices[index]);
        }

        // An undo is a closing window and outranks anything that merely waits, so it keeps the
        // corner; the notices reappear the moment it is gone.
        if (host.FindViewWithTag(HostTag) is not null)
        {
            return;
        }

        RepaintNotices(context, host);
    }

    /// <summary>Flattens the notice fold and forgets everything that was waiting.</summary>
    public static void ClearNotices(FrameLayout host)
    {
        Waiting.GetOrCreateValue(host).Clear();
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
        var size = PaperTheme.Dp(context, DogEarGeometry.SizeDp);
        var fold = new FoldView(context, notices[0].Headline, timer: null, showsBackArrow: false, hue: notices[0].Hue)
        {
            Tag = NoticeTag,
        };
        fold.Click += (_, _) => OpenNotices(context, host);

        fold.Alpha = 0f;
        host.AddView(fold, new FrameLayout.LayoutParams(size, size, GravityFlags.Top | GravityFlags.End));
        fold.Animate()!.Alpha(1f).SetDuration(DesignTokens.Motion.StandardMs).Start();
    }

    // Turning a NOTICE fold no longer opens the ActionCard popup (D162): it turns the page to a
    // full Waiting screen instead — a Serif header, the rule, and every notice written straight
    // onto the page, newest first, with no count. The undo fold above (Show/Dismiss) is unchanged;
    // this is only what happens when there is nothing to reverse and the corner turns TO something.
    private static void OpenNotices(Context context, FrameLayout host)
    {
        var notices = Waiting.GetOrCreateValue(host).Notices;
        if (notices.Count == 0)
        {
            return;
        }

        WaitingActivity.Launch(context, notices);
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

        var size = PaperTheme.Dp(context, DogEarGeometry.SizeDp);
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
        // How many straight segments the arrow's arc is flattened to; at 40dp indistinguishable
        // from the true curve.
        private const int ArrowSegments = 32;

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

            // A dog-ear is TWO triangles, and shipping only the first is what made this
            // read as a coloured corner tag rather than a folded page. The shape is mockup 17's,
            // from DogEarGeometry — the same numbers the MAUI kit's painter draws.
            //
            // The crease runs (0,0) to (w,h). Above it is the HOLE: the part of the page
            // that is no longer there, so what shows is whatever the fold is carrying.
            // Below it is the FLAP: that same corner turned over and lying back across
            // the page, which is why it is paper — you are looking at the back of the
            // sheet — and why it is the half nearer the reader.
            using var underside = new Paint { AntiAlias = true, Color = hue };
            using var hole = new Android.Graphics.Path();
            hole.MoveTo(w, 0);
            hole.LineTo(0, 0);
            hole.LineTo(w, h);
            hole.Close();
            canvas.DrawPath(hole, underside);

            // The flap: flat paper, its loose tip lifted off the page (6,34 of 40) rather than
            // running to the foot of the fold.
            using var flap = new Android.Graphics.Path();
            flap.MoveTo(0, 0);
            flap.LineTo(w, h);
            flap.LineTo(
                DogEarGeometry.Scale(DogEarGeometry.FlapTip.X, w),
                DogEarGeometry.Scale(DogEarGeometry.FlapTip.Y, h));
            flap.Close();

            using var back = new Paint { AntiAlias = true, Color = PaperTheme.PaperDeep };
            canvas.DrawPath(flap, back);

            // The crease: a hairline between the halves.
            using var crease = new Paint { AntiAlias = true, StrokeWidth = density, Color = PaperTheme.Hairline };
            crease.SetStyle(Paint.Style.Stroke);
            canvas.DrawLine(0, 0, w, h, crease);

            if (!showsBackArrow)
            {
                return;
            }

            // The back-turn arrow, and ONLY on the undo fold: it is what says this one reverses
            // the act rather than turning to something waiting. Two round-capped strokes — an arc
            // and a head — from the mockup's own SVG; it used to be a glyph set in a serif font.
            using var mark = new Paint(PaintFlags.AntiAlias)
            {
                Color = PaperTheme.PaperBright,
                StrokeWidth = DogEarGeometry.Scale(DogEarGeometry.ArrowStroke, w),
                StrokeCap = Paint.Cap.Round,
                StrokeJoin = Paint.Join.Round,
            };
            mark.SetStyle(Paint.Style.Stroke);

            using var arc = new Android.Graphics.Path();
            var points = DogEarGeometry.ArrowArc(ArrowSegments);
            arc.MoveTo(DogEarGeometry.Scale(points[0].X, w), DogEarGeometry.Scale(points[0].Y, h));
            for (var i = 1; i < points.Count; i++)
            {
                arc.LineTo(DogEarGeometry.Scale(points[i].X, w), DogEarGeometry.Scale(points[i].Y, h));
            }

            canvas.DrawPath(arc, mark);

            using var head = new Android.Graphics.Path();
            var wings = DogEarGeometry.ArrowHead;
            head.MoveTo(DogEarGeometry.Scale(wings[0].X, w), DogEarGeometry.Scale(wings[0].Y, h));
            head.LineTo(DogEarGeometry.Scale(wings[1].X, w), DogEarGeometry.Scale(wings[1].Y, h));
            head.LineTo(DogEarGeometry.Scale(wings[2].X, w), DogEarGeometry.Scale(wings[2].Y, h));
            canvas.DrawPath(head, mark);
        }
    }
}
