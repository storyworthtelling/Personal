using Android.App;
using Android.OS;

namespace Codex.Ui;

/// <summary>
/// The page-slide screen transition (DESIGN.md §5). Min SDK is 34, so the
/// modern OverrideActivityTransition API applies unconditionally. Call from
/// OnCreate of every activity.
/// </summary>
public static class PaperTransitions
{
    public static void Apply(Activity activity)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
        {
            activity.OverrideActivityTransition(OverrideTransition.Open,
                Resource.Animation.paper_slide_in, Resource.Animation.paper_slide_out);
            activity.OverrideActivityTransition(OverrideTransition.Close,
                Resource.Animation.paper_slide_back_in, Resource.Animation.paper_slide_back_out);
        }
    }
}
