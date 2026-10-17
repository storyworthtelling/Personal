using Android.App;
using Android.Views;

namespace Constitution;

/// <summary>
/// The Today tab. A neutral page for now: the content, and the ground's painters, arrive with
/// their releases. It has no action yet, so it has no pinned bar.
/// </summary>
public sealed class TodayArea(Activity host)
    : ConstitutionAreaBase(host, "today", Resource.Drawable.areatoday)
{
    protected override View BuildPage() => PageFurniture.Sentence(Host, "Nothing is planned yet.");
}
