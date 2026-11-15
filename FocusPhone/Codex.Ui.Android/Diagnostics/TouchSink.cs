using Android.Content;
using Android.Views;
using Android.Widget;

namespace Codex.Ui;

/// <summary>
/// A view that takes touches only so they do not fall through to what lies under it — a card's body over its
/// haze, the haze over the page. It is not a control: <see cref="TapWatch"/> stops at it, names it by its type
/// alone (its first text is a card's title, often a person) and never judges a tap on it a dead button.
/// </summary>
public interface ITouchSink
{
}

/// <summary>A frame that is a touch sink: the action card's haze, whose own click dismisses the card.</summary>
public sealed class TouchSinkFrame(Context context) : FrameLayout(context), ITouchSink;

/// <summary>A vertical column that swallows every touch on its blank area, so it never reaches the haze beneath.</summary>
public sealed class TouchSinkColumn : LinearLayout, ITouchSink, IDrawnControls
{
    public TouchSinkColumn(Context context) : base(context)
    {
        Orientation = Orientation.Vertical;
    }

    public override bool OnTouchEvent(MotionEvent? e) => true;

    /// <summary>It swallows touches but draws no control of its own, so nothing under the finger is one.</summary>
    public bool HasControlAt(float x, float y) => false;
}
