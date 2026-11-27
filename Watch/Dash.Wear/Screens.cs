using Android.App;
using Android.Content;
using Android.Views;
using Android.Widget;
using Watch.Domain.Dash;
using Watch.Domain.Design;
using Watch.Ui.Wear;

namespace Dash.Wear;

/// <summary>The round screen in pixels: the mockups place everything in fractions of its radius (<see cref="DashLayout"/>).</summary>
internal sealed class Round
{
    public Round(Context context)
    {
        var metrics = context.Resources!.DisplayMetrics!;
        Radius = Math.Min(metrics.WidthPixels, metrics.HeightPixels) / 2f;
    }

    public float Radius { get; }

    public int Px(double fraction) => (int)Math.Round(fraction * Radius);
}

/// <summary>One Dash screen's frame: true black, the time pinned at the top, and a column the crown scrolls (WD §9).</summary>
internal sealed record Frame(FrameLayout Root, ScrollView Scroll, LinearLayout Column, WatchTextView Status);

/// <summary>
/// What every Dash screen shares (P14 B4): the frame, the quiet line, a pinned pill and the way an answer from the pool
/// comes back to the screen. A screen that has gone when its answer comes is left alone.
/// </summary>
internal static class Screens
{
    public static Frame Build(Activity screen, Round round, int top, int bottom)
    {
        var root = new FrameLayout(screen);
        root.SetBackgroundColor(WatchPaints.Of(WatchTokens.Ground));

        var column = new LinearLayout(screen) { Orientation = Orientation.Vertical };
        column.SetPadding(0, top, 0, bottom);
        var scroll = new ScrollView(screen)
        {
            // The crown scrolls a focused scroll view (WD §9).
            Focusable = true,
            FocusableInTouchMode = true,
            VerticalScrollBarEnabled = false,
        };
        scroll.AddView(column, new ViewGroup.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
        root.AddView(scroll, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

        var time = new WatchTimeView(screen);
        var timeTop = Math.Max(0, round.Px(DashLayout.TimeCentre) - time.LineHeight / 2);
        root.AddView(time, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = timeTop,
        });

        var status = new WatchTextView(screen) { Tone = WatchTone.Soft, Visibility = ViewStates.Gone };
        status.SetPadding(round.Px(DashLayout.RowLeft), round.Px(DashLayout.BubbleGap), round.Px(DashLayout.RowLeft), round.Px(DashLayout.BubbleGap));

        screen.SetContentView(root);
        scroll.RequestFocus();
        return new Frame(root, scroll, column, status);
    }

    /// <summary>The quiet line (WD §6): a status sentence while something is on its way, an error when it went wrong.</summary>
    public static void Say(WatchTextView status, string? sentence)
    {
        if (sentence is null)
        {
            status.Visibility = ViewStates.Gone;
            return;
        }
        status.Sentence = sentence;
        status.Visibility = ViewStates.Visible;
    }

    /// <summary>A pill pinned at <paramref name="centre"/> (a fraction of the radius from the top), as wide as the mockup draws it or its words need.</summary>
    public static WatchPillView Pin(Activity screen, Frame frame, Round round, string label, double width, double centre)
    {
        var pill = new WatchPillView(screen) { App = WatchApp.Dash, Label = label, DrawnHeight = round.Px(DashLayout.PillHeight) };
        var inset = round.Px(DashLayout.RowPadding);
        pill.SetPadding(inset, 0, inset, 0);
        pill.SetMinWidth(round.Px(width));
        var height = (int)Math.Ceiling(WatchPaints.TouchTarget(screen));
        frame.Root.AddView(pill, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, height, GravityFlags.Top | GravityFlags.CenterHorizontal)
        {
            TopMargin = round.Px(centre) - height / 2,
        });
        return pill;
    }

    /// <summary>A quieter choice beside a pill (mock: Keyboard, Again): a word in the grey that is a whole touch target.</summary>
    public static WatchTextView Choice(Activity screen, string label)
    {
        var choice = new WatchTextView(screen) { Tone = WatchTone.Soft, Label = label, Clickable = true, Focusable = true };
        var target = (int)Math.Ceiling(WatchPaints.TouchTarget(screen));
        choice.SetMinHeight(target);
        choice.SetMinWidth(target);
        return choice;
    }

    /// <summary>The asked-for answer, back on the screen's own thread, if the screen is still there.</summary>
    public static void WhenAnswered(Activity screen, Task<DashReply> asking, Action<DashReply> show) =>
        asking.ContinueWith(
            done =>
            {
                var reply = done.IsFaulted ? Stopped(done.Exception!) : done.Result;
                screen.RunOnUiThread(() =>
                {
                    if (!screen.IsFinishing && !screen.IsDestroyed)
                    {
                        show(reply);
                    }
                });
            },
            CancellationToken.None,
            TaskContinuationOptions.None,
            TaskScheduler.Default);

    /// <summary>A tell nobody answers: what stood in the way is logged by the link, and anything unforeseen here.</summary>
    public static void Told(Task<DashTrouble?> telling) =>
        telling.ContinueWith(
            static done => DashLog.Quiet("DASH a tell to the phone stopped", done.Exception!.GetBaseException()),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

    // Anything the link did not foresee: logged by its shape, and said as no answer.
    private static DashReply Stopped(AggregateException failure)
    {
        DashLog.Quiet("DASH an ask to the phone stopped", failure.GetBaseException());
        return DashReply.Failed(DashTrouble.NoAnswer);
    }
}
