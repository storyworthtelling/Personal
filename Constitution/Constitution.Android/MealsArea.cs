using Android.App;
using Android.Views;

namespace Constitution;

/// <summary>
/// The Meals tab. A neutral page for now: the content, and the ground's painters, arrive with
/// their releases. It has no action yet, so it has no pinned bar.
/// </summary>
public sealed class MealsArea(Activity host)
    : ConstitutionAreaBase(host, "meals", Resource.Drawable.areameals)
{
    protected override View BuildPage() => PageFurniture.Sentence(Host, "No meals are planned yet.");
}
