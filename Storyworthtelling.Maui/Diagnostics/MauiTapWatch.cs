using System.Runtime.CompilerServices;
using Android.OS;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using Storyworthtelling.Diagnostics;
using AView = Android.Views.View;
using MauiView = Microsoft.Maui.Controls.View;

namespace Storyworthtelling.Maui.Diagnostics;

/// <summary>
/// The MAUI heads' tap watch (LOG-LOOP-PLAN.md §4.1), judged by the same <see cref="TapWatchCore"/> as the native
/// heads. Three feeds:
/// <list type="bullet">
/// <item><c>MainActivity.DispatchTouchEvent</c> hands every touch to <see cref="Touch"/>, which hit-tests the native
/// view under the finger. A control is a clickable view (every MAUI <c>Button</c>) or one whose MAUI view carries a
/// <c>TapGestureRecognizer</c> — the handler mapper keeps that link, since a gesture leaves its view unclickable.</item>
/// <item>The page appearing (<c>Application.PageAppearing</c>) is the screen changing.</item>
/// <item>The window's global layout, when the visible tree's fingerprint (how many views, what their text says)
/// changes, is the content changing.</item>
/// </list>
/// A key action is declared through <see cref="Action"/>. Nothing here may throw into the app: a broken watch logs
/// to logcat and lets the touch through.
/// </summary>
public static class MauiTapWatch
{
    private static readonly ConditionalWeakTable<AView, MauiView> Views = new();
    private static TapWatchCore? core;
    private static Handler? main;
    private static int tickScheduled;
    private static (float X, float Y, long Time)? down;

    /// <summary>Once, from <c>MauiProgram</c>; verdicts go to <paramref name="report"/>.</summary>
    public static void Install(Action<TapVerdict> report)
    {
        core = new TapWatchCore(SystemClock.UptimeMillis, report);
        main = new Handler(Looper.MainLooper!);
        ViewHandler.ViewMapper.AppendToMapping("TapWatch", (handler, view) =>
        {
            if (handler.PlatformView is AView platform && view is MauiView mauiView)
            {
                Views.AddOrUpdate(platform, mauiView);
            }
        });
        if (Microsoft.Maui.Controls.Application.Current is { } app)
        {
            Watch(app);
        }
    }

    /// <summary>From <c>MainActivity.OnCreate</c>: the screen and layout feeds for the one MAUI activity.</summary>
    public static void Attach(Android.App.Activity activity)
    {
        try
        {
            if (Microsoft.Maui.Controls.Application.Current is { } app)
            {
                Watch(app);
            }
            activity.Window?.DecorView.ViewTreeObserver?.AddOnGlobalLayoutListener(new LayoutListener(activity));
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("TapWatch", $"Could not watch {activity.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>From <c>MainActivity.DispatchTouchEvent</c>, before the touch is dispatched.</summary>
    public static void Touch(Android.App.Activity activity, MotionEvent? e)
    {
        try
        {
            if (e is null || core is not { } watch)
            {
                return;
            }
            switch (e.ActionMasked)
            {
                case MotionEventActions.Down:
                    down = (e.GetX(), e.GetY(), e.EventTime);
                    break;
                case MotionEventActions.Cancel:
                    down = null;
                    break;
                case MotionEventActions.Up when activity.Window?.DecorView is { } root:
                    var start = down;
                    down = null;
                    Touched(watch, root, e, start, activity);
                    break;
            }
        }
        catch (Exception ex)
        {
            Android.Util.Log.Warn("TapWatch", $"Touch watch failed: {ex.Message}");
        }
    }

    /// <summary>Declares a key action (O10): never judged dead; must report within <paramref name="within"/>.</summary>
    public static KeyAction Action(string actionId, TimeSpan within)
    {
        if (core is not { } watch)
        {
            return KeyAction.Unwatched(actionId);
        }
        var action = watch.Action(actionId, within);
        Schedule();
        return action;
    }

    /// <summary>Runs an awaited act as a declared key action: it succeeds when the act returns and fails, rethrown, when it throws.</summary>
    public static async Task RunAsync(KeyAction action, Func<Task> act)
    {
        try
        {
            await act().ConfigureAwait(true);
            action.Succeeded();
        }
        catch (Exception e)
        {
            action.Failed($"{e.GetType().Name}: {e.Message}");
            throw;
        }
    }

    private static bool watchingApp;

    private static void Watch(Microsoft.Maui.Controls.Application app)
    {
        if (watchingApp)
        {
            return;
        }
        watchingApp = true;
        app.PageAppearing += (_, page) =>
        {
            core?.ScreenShown(page.GetType().Name);
            Schedule();
        };
    }

    private static void Touched(TapWatchCore watch, AView root, MotionEvent up, (float X, float Y, long Time)? start, Android.App.Activity activity)
    {
        var x = up.GetX();
        var y = up.GetY();
        if (start is { } s)
        {
            var slop = ViewConfiguration.Get(activity)?.ScaledTouchSlop ?? 24;
            if (Math.Abs(x - s.X) > slop || Math.Abs(y - s.Y) > slop)
            {
                return; // a scroll or a swipe, not a tap
            }
        }
        var longPress = start is { } t && up.EventTime - t.Time >= ViewConfiguration.LongPressTimeout;
        var hit = Deepest(root, (int)x, (int)y);
        if (hit is null)
        {
            return;
        }
        var control = hit;
        while (control is not null && !IsControl(control, longPress))
        {
            control = control.Parent as AView;
        }
        var target = control ?? hit;
        var name = Name(target, activity) + (longPress ? " (long press)" : string.Empty);
        watch.Touched(name, new TapPoint(x, y), target.Enabled, control is not null);
        Schedule();
    }

    private static bool IsControl(AView view, bool longPress)
    {
        if (longPress ? view.LongClickable : view.Clickable || view.HasOnClickListeners)
        {
            return true;
        }
        return Views.TryGetValue(view, out var mauiView)
            && mauiView.GestureRecognizers.Any(g => g is Microsoft.Maui.Controls.TapGestureRecognizer);
    }

    private static string Name(AView view, Android.App.Activity activity)
    {
        string? id = null;
        if (view.Id != AView.NoId)
        {
            try
            {
                id = activity.Resources?.GetResourceEntryName(view.Id);
            }
            catch (Android.Content.Res.Resources.NotFoundException)
            {
                // A generated id has no entry name; the label is enough.
            }
        }
        var text = view is TextView own ? own.Text : FirstText(view);
        if (string.IsNullOrWhiteSpace(text))
        {
            text = view.ContentDescription;
        }
        if (string.IsNullOrWhiteSpace(text) && Views.TryGetValue(view, out var mauiView))
        {
            text = Microsoft.Maui.Controls.AutomationProperties.GetName(mauiView);
        }
        return TapWatchCore.ControlName(text, id, Views.TryGetValue(view, out var named) ? named.GetType().Name : view.GetType().Name);
    }

    private static string? FirstText(AView view)
    {
        if (view is TextView textView && !string.IsNullOrWhiteSpace(textView.Text))
        {
            return textView.Text;
        }
        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                if (group.GetChildAt(i) is { Visibility: ViewStates.Visible } child && FirstText(child) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    // The topmost visible view under the point, in window coordinates.
    private static AView? Deepest(AView view, int x, int y)
    {
        if (view.Visibility != ViewStates.Visible || !Contains(view, x, y))
        {
            return null;
        }
        if (view is ViewGroup group)
        {
            for (var i = group.ChildCount - 1; i >= 0; i--)
            {
                if (group.GetChildAt(i) is { } child && Deepest(child, x, y) is { } found)
                {
                    return found;
                }
            }
        }
        return view;
    }

    private static bool Contains(AView view, int x, int y)
    {
        var location = new int[2];
        view.GetLocationInWindow(location);
        return x >= location[0] && x < location[0] + view.Width && y >= location[1] && y < location[1] + view.Height;
    }

    // While anything is pending, tick on the main looper so verdicts mature without a further event.
    private static void Schedule()
    {
        var watch = core;
        var handler = main;
        if (watch is null || handler is null || watch.NextTickDelayMs() is not { } delay)
        {
            return;
        }
        if (Interlocked.CompareExchange(ref tickScheduled, 1, 0) != 0)
        {
            return;
        }
        handler.PostDelayed(() =>
        {
            Interlocked.Exchange(ref tickScheduled, 0);
            watch.Tick();
            Schedule();
        }, delay);
    }

    // The visible tree's fingerprint: how many views are showing and what their text says. A layout pass that leaves
    // it unchanged (a blinking cursor, a re-measure) is not a content change.
    private sealed class LayoutListener(Android.App.Activity activity) : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private long last;

        public void OnGlobalLayout()
        {
            try
            {
                if (activity.Window?.DecorView is not { } root)
                {
                    return;
                }
                var hash = new HashCode();
                var count = 0;
                Walk(root, ref hash, ref count);
                hash.Add(count);
                var fingerprint = (long)hash.ToHashCode();
                if (fingerprint != last)
                {
                    last = fingerprint;
                    core?.ContentChanged();
                    Schedule();
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("TapWatch", $"Layout fingerprint failed: {e.Message}");
            }
        }

        private static void Walk(AView view, ref HashCode hash, ref int count)
        {
            if (view.Visibility != ViewStates.Visible)
            {
                return;
            }
            count++;
            if (view is TextView text)
            {
                hash.Add(text.Text);
            }
            if (view is ViewGroup group)
            {
                for (var i = 0; i < group.ChildCount; i++)
                {
                    if (group.GetChildAt(i) is { } child)
                    {
                        Walk(child, ref hash, ref count);
                    }
                }
            }
        }
    }
}
