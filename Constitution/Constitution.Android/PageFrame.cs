using Android.Content;
using Android.Views;
using Constitution.Domain.Layout;

namespace Constitution;

/// <summary>
/// The page: header, well, and (where the area has an action) the pinned bar. Where each sits is
/// decided by the pure <see cref="PageFrameLayout"/>, so the well ends exactly at the bar's top
/// and nothing scrolls behind it (D59). Heights are measured in pixels.
/// </summary>
public sealed class PageFrame(Context context) : ViewGroup(context)
{
    private View? header;
    private View? well;
    private View? bar;
    private PageBands bands;

    public void SetHeader(View view) => Replace(ref header, view);

    public void SetWell(View view) => Replace(ref well, view);

    /// <summary>Null when the area has no action.</summary>
    public void SetBar(View? view) => Replace(ref bar, view);

    private void Replace(ref View? slot, View? view)
    {
        if (slot is not null)
        {
            RemoveView(slot);
        }

        slot = view;
        if (view is not null)
        {
            AddView(view);
        }
    }

    protected override void OnMeasure(int widthMeasureSpec, int heightMeasureSpec)
    {
        var width = MeasureSpec.GetSize(widthMeasureSpec);
        var height = MeasureSpec.GetSize(heightMeasureSpec);
        var across = MeasureSpec.MakeMeasureSpec(width, MeasureSpecMode.Exactly);
        var atMost = MeasureSpec.MakeMeasureSpec(height, MeasureSpecMode.AtMost);

        header?.Measure(across, atMost);
        bar?.Measure(across, atMost);

        bands = PageFrameLayout.Compute(
            height,
            header?.MeasuredHeight ?? 0,
            bar?.MeasuredHeight);

        well?.Measure(across, MeasureSpec.MakeMeasureSpec(bands.WellBottom - bands.WellTop, MeasureSpecMode.Exactly));
        SetMeasuredDimension(width, height);
    }

    protected override void OnLayout(bool changed, int left, int top, int right, int bottom)
    {
        var width = right - left;
        header?.Layout(0, bands.HeaderTop, width, bands.HeaderBottom);
        well?.Layout(0, bands.WellTop, width, bands.WellBottom);
        if (bar is not null && bands.BarTop is { } barTop && bands.BarBottom is { } barBottom)
        {
            bar.Layout(0, barTop, width, barBottom);
        }
    }
}
