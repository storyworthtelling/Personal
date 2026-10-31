using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Diagnostics;
using Storyworthtelling.Diagnostics;

namespace Codex.Ui;

/// <summary>
/// The native tap watch (LOG-LOOP-PLAN.md §4.1, G1): finds buttons that do nothing, the same control pressed
/// again and again, taps on things that only look tappable, handlers that throw and key actions that fail
/// or never report. The judging is <see cref="TapWatchCore"/> over the shared <see cref="TapJudge"/>; this is
/// the glue that feeds it:
/// <list type="bullet">
/// <item>PaperUi's four button factories run their onClick through <see cref="Wrap"/>.</item>
/// <item><see cref="Attach"/> wraps an activity's window callback, so it sees every touch — raw
/// <c>Click +=</c> handlers and non-controls included — hit-tests the view under the finger, and listens
/// to global layout for the visible tree's fingerprint changing.</item>
/// <item>Background work declares itself with <see cref="Action"/> and reports how it ended.</item>
/// </list>
/// Taps are never logged on their own (O8): only a verdict is, with the last 20 taps as its context.
/// Nothing here may break the app; a watch that is not installed is a pass-through.
/// </summary>
public static class TapWatch
{
    private static TapWatchCore? core;
    private static Handler? main;
    private static int tickScheduled;

    /// <summary>Once, from the head's Application.OnCreate; verdicts go to <paramref name="report"/>.</summary>
    public static void Install(Action<TapVerdict> report)
    {
        core = new TapWatchCore(SystemClock.UptimeMillis, report);
        main = new Handler(Looper.MainLooper!);
    }

    /// <summary>Watches an activity: call from OnCreate, after SetContentView or before — either works.</summary>
    public static void Attach(Activity activity, string? screen = null)
    {
        var watch = core;
        var window = activity.Window;
        if (watch is null || window is null || window.Callback is WatchedCallback || window.Callback is null)
        {
            return;
        }
        try
        {
            window.Callback = new WatchedCallback(window.Callback, activity);
            window.DecorView.ViewTreeObserver?.AddOnGlobalLayoutListener(new LayoutListener(window));
            watch.ScreenShown(screen ?? activity.GetType().Name);
            Schedule();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("TapWatch", $"Could not watch {activity.GetType().Name}: {e}");
        }
    }

    /// <summary>The screen inside an activity changed (an area or tab switched).</summary>
    public static void Screen(string name)
    {
        core?.ScreenShown(name);
        Schedule();
    }

    /// <summary>A status line or dog-ear changed without a layout pass.</summary>
    public static void Status()
    {
        core?.StatusChanged();
        Schedule();
    }

    /// <summary>What a click handler runs through. Resolved at click time, so it works before Install.</summary>
    public static Action Wrap(string label, Action onClick)
    {
        var control = TapWatchCore.ControlName(label, null, nameof(Button));
        return () =>
        {
            var watch = core;
            if (watch is null)
            {
                onClick();
                return;
            }
            try
            {
                watch.Wrap(control, onClick)();
            }
            finally
            {
                Schedule();
            }
        };
    }

    /// <summary>Declares a key action (O10): never judged dead; must report within <paramref name="within"/>.</summary>
    public static KeyAction Action(string actionId, TimeSpan within)
    {
        var watch = core;
        if (watch is null)
        {
            return KeyAction.Unwatched(actionId);
        }
        var action = watch.Action(actionId, within);
        Schedule();
        return action;
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

    private static void Touched(View root, MotionEvent up, DownEvent? down, Activity activity)
    {
        var watch = core;
        if (watch is null)
        {
            return;
        }
        var x = up.GetX();
        var y = up.GetY();
        if (down is not null)
        {
            var slop = ViewConfiguration.Get(activity)?.ScaledTouchSlop ?? 24;
            if (Math.Abs(x - down.X) > slop || Math.Abs(y - down.Y) > slop)
            {
                return; // a scroll or a swipe, not a tap
            }
        }
        var longPress = down is not null && up.EventTime - down.Time >= ViewConfiguration.LongPressTimeout;

        var hit = Deepest(root, (int)x, (int)y);
        if (hit is null)
        {
            return;
        }
        var control = hit;
        while (control is not null && !IsControl(control, longPress))
        {
            control = control.Parent as View;
        }
        var target = control ?? hit;
        var name = Name(target, activity) + (longPress ? " (long press)" : string.Empty);
        watch.Touched(name, new TapPoint(x, y), target.Enabled, control is not null);
        Schedule();
    }

    private static bool IsControl(View view, bool longPress) =>
        longPress ? view.LongClickable : view.Clickable || view.HasOnClickListeners;

    private static string Name(View view, Activity activity)
    {
        string? id = null;
        if (view.Id != View.NoId)
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
        return TapWatchCore.ControlName(text, id, view.GetType().Name);
    }

    private static string? FirstText(View view)
    {
        if (view is TextView textView && !string.IsNullOrWhiteSpace(textView.Text))
        {
            return textView.Text;
        }
        if (view is ViewGroup group)
        {
            for (var i = 0; i < group.ChildCount; i++)
            {
                var child = group.GetChildAt(i);
                if (child is { Visibility: ViewStates.Visible } && FirstText(child) is { } found)
                {
                    return found;
                }
            }
        }
        return null;
    }

    // The topmost visible view under the point, in window coordinates.
    private static View? Deepest(View view, int x, int y)
    {
        if (view.Visibility != ViewStates.Visible || !Contains(view, x, y))
        {
            return null;
        }
        if (view is ViewGroup group)
        {
            for (var i = group.ChildCount - 1; i >= 0; i--)
            {
                var child = group.GetChildAt(i);
                if (child is not null && Deepest(child, x, y) is { } found)
                {
                    return found;
                }
            }
        }
        return view;
    }

    private static bool Contains(View view, int x, int y)
    {
        var location = new int[2];
        view.GetLocationInWindow(location);
        return x >= location[0] && x < location[0] + view.Width && y >= location[1] && y < location[1] + view.Height;
    }

    // The visible tree's fingerprint: how many views are showing and what their text says. A layout pass
    // that leaves it unchanged (a blinking cursor, a re-measure) is not a content change.
    private sealed class LayoutListener(Window window) : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
    {
        private long last;

        public void OnGlobalLayout()
        {
            try
            {
                var hash = new HashCode();
                var count = 0;
                Walk(window.DecorView, ref hash, ref count);
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

        private static void Walk(View view, ref HashCode hash, ref int count)
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

    private readonly record struct Down(float X, float Y, long Time);

    // Delegates every call to the activity's own callback; only touch dispatch is looked at, and the tap is
    // fed before it is dispatched, because a click handler runs after ACTION_UP is delivered.
    private sealed class WatchedCallback(Window.ICallback inner, Activity activity) : Java.Lang.Object, Window.ICallback
    {
        private Down? down;

        public bool DispatchTouchEvent(MotionEvent? e)
        {
            try
            {
                if (e is not null)
                {
                    switch (e.ActionMasked)
                    {
                        case MotionEventActions.Down:
                            down = new Down(e.GetX(), e.GetY(), e.EventTime);
                            break;
                        case MotionEventActions.Up when activity.Window?.DecorView is { } root:
                            var start = down;
                            down = null;
                            Touched(root, e, start is null ? null : new DownEvent(start.Value), activity);
                            break;
                        case MotionEventActions.Cancel:
                            down = null;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                Android.Util.Log.Warn("TapWatch", $"Touch watch failed: {ex.Message}");
            }
            return inner.DispatchTouchEvent(e);
        }

        public bool DispatchGenericMotionEvent(MotionEvent? e) => inner.DispatchGenericMotionEvent(e);
        public bool DispatchKeyEvent(KeyEvent? e) => inner.DispatchKeyEvent(e);
        public bool DispatchKeyShortcutEvent(KeyEvent? e) => inner.DispatchKeyShortcutEvent(e);
        public bool DispatchPopulateAccessibilityEvent(Android.Views.Accessibility.AccessibilityEvent? e) => inner.DispatchPopulateAccessibilityEvent(e);
        public bool DispatchTrackballEvent(MotionEvent? e) => inner.DispatchTrackballEvent(e);
        public void OnActionModeFinished(ActionMode? mode) => inner.OnActionModeFinished(mode);
        public void OnActionModeStarted(ActionMode? mode) => inner.OnActionModeStarted(mode);
        public void OnAttachedToWindow() => inner.OnAttachedToWindow();
        public void OnContentChanged() => inner.OnContentChanged();
        public bool OnCreatePanelMenu(int featureId, IMenu menu) => inner.OnCreatePanelMenu(featureId, menu);
        public View? OnCreatePanelView(int featureId) => inner.OnCreatePanelView(featureId);
        public void OnDetachedFromWindow() => inner.OnDetachedFromWindow();
        public bool OnMenuItemSelected(int featureId, IMenuItem item) => inner.OnMenuItemSelected(featureId, item);
        public bool OnMenuOpened(int featureId, IMenu menu) => inner.OnMenuOpened(featureId, menu);
        public void OnPanelClosed(int featureId, IMenu menu) => inner.OnPanelClosed(featureId, menu);
        public bool OnPreparePanel(int featureId, View? view, IMenu menu) => inner.OnPreparePanel(featureId, view, menu);
        public bool OnSearchRequested() => inner.OnSearchRequested();
        public bool OnSearchRequested(SearchEvent? searchEvent) => inner.OnSearchRequested(searchEvent);
        public void OnWindowAttributesChanged(WindowManagerLayoutParams? attrs) => inner.OnWindowAttributesChanged(attrs);
        public void OnWindowFocusChanged(bool hasFocus) => inner.OnWindowFocusChanged(hasFocus);
        public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback) => inner.OnWindowStartingActionMode(callback);
        public ActionMode? OnWindowStartingActionMode(ActionMode.ICallback? callback, ActionModeType type) => inner.OnWindowStartingActionMode(callback, type);
        public void OnPointerCaptureChanged(bool hasCapture) => inner.OnPointerCaptureChanged(hasCapture);
        public void OnProvideKeyboardShortcuts(IList<KeyboardShortcutGroup>? data, IMenu? menu, int deviceId) => inner.OnProvideKeyboardShortcuts(data, menu, deviceId);
    }

    private sealed class DownEvent(Down down)
    {
        public float X => down.X;
        public float Y => down.Y;
        public long Time => down.Time;
    }
}
