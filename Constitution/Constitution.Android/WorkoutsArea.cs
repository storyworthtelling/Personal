using Android.App;
using Android.Views;

namespace Constitution;

/// <summary>
/// The Workouts tab. A neutral page for now: the content, and the ground's painters, arrive with
/// their releases. It has no action yet, so it has no pinned bar.
/// </summary>
public sealed class WorkoutsArea(Activity host)
    : ConstitutionAreaBase(host, "workouts", Resource.Drawable.areaworkouts)
{
    protected override View BuildPage() => PageFurniture.Sentence(Host, "No workouts are planned yet.");
}
