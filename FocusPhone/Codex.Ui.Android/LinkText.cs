using Android.Text;
using Android.Text.Method;
using Android.Text.Style;
using Android.Text.Util;
using Android.Views;
using Android.Widget;

namespace Codex.Ui;

/// <summary>
/// Web links in a line of text, made tappable without taking the row they sit in. The platform's
/// own <see cref="URLSpan"/> sends the link to the default browser, which on the locked phone is
/// not where a stranger's link should go, so each is swapped for a span that calls the caller.
/// </summary>
public static class LinkText
{
    public static ISpanned Build(string text, Action<string> open)
    {
        var spannable = new SpannableString(text);
        Linkify.AddLinks(spannable, MatchOptions.WebUrls);
        var found = spannable.GetSpans(0, spannable.Length(), Java.Lang.Class.FromType(typeof(URLSpan)));
        if (found is not null)
        {
            foreach (var span in found.OfType<URLSpan>().ToList())
            {
                var start = spannable.GetSpanStart(span);
                var end = spannable.GetSpanEnd(span);
                var url = span.URL ?? string.Empty;
                spannable.RemoveSpan(span);
                spannable.SetSpan(new OpenSpan(url, open), start, end, SpanTypes.ExclusiveExclusive);
            }
        }
        return spannable;
    }

    private sealed class OpenSpan(string url, Action<string> open) : ClickableSpan
    {
        public override void OnClick(View widget) => open(url);
    }

    /// <summary>Handles a tap only when it lands on a link; every other touch is left to the row.</summary>
    public sealed class TapOnlyMethod : LinkMovementMethod
    {
        public override bool OnTouchEvent(TextView? widget, ISpannable? buffer, MotionEvent? e)
        {
            if (widget is null || buffer is null || e is null
                || (e.Action != MotionEventActions.Up && e.Action != MotionEventActions.Down))
            {
                return false;
            }
            var x = (int)e.GetX() - widget.TotalPaddingLeft + widget.ScrollX;
            var y = (int)e.GetY() - widget.TotalPaddingTop + widget.ScrollY;
            var layout = widget.Layout;
            if (layout is null)
            {
                return false;
            }
            var offset = layout.GetOffsetForHorizontal(layout.GetLineForVertical(y), x);
            var spans = buffer.GetSpans(offset, offset, Java.Lang.Class.FromType(typeof(ClickableSpan)));
            var span = spans?.OfType<ClickableSpan>().FirstOrDefault();
            if (span is null)
            {
                return false;
            }
            if (e.Action == MotionEventActions.Up)
            {
                span.OnClick(widget);
            }
            return true;
        }
    }
}
