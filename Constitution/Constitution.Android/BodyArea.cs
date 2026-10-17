using Android.App;
using Android.Views;

namespace Constitution;

/// <summary>
/// The Body tab. A neutral page for now: the content, and the ground's painters, arrive with
/// their releases. It has no action yet, so it has no pinned bar.
/// </summary>
public sealed class BodyArea(Activity host)
    : ConstitutionAreaBase(host, "body", Resource.Drawable.areabody)
{
    protected override View BuildPage() => PageFurniture.Sentence(Host, "Nothing has been recorded yet.");
}
