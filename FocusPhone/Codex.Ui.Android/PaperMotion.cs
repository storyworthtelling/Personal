using Android.Animation;
using Android.Content;
using AccessibilityManager = Android.Views.Accessibility.AccessibilityManager;
using ContentMode = Android.Views.Accessibility.ContentMode;

namespace Codex.Ui;

/// <summary>
/// What motion and timing the person has asked Android for (DESIGN.md §6, §17, audit W9.3, report 10 §23).
///
/// <para>The view animations and page slides already follow "Remove animations" by themselves. This is for what the
/// platform cannot see: anything drawn frame by frame on a canvas (a page curl, a bloom) must ask
/// <see cref="Reduced"/> first, and a line held up to be read must last <see cref="ReadingTime"/>, which honours
/// "Time to take action".</para>
/// </summary>
public static class PaperMotion
{
    /// <summary>True when the person has turned animations off: draw the end state, not the movement.</summary>
    public static bool Reduced => !ValueAnimator.AreAnimatorsEnabled();

    /// <summary>How long to hold words up to be read: the design's beat, or the person's longer one.</summary>
    public static int ReadingTime(Context context, int designMs)
    {
        if (context.GetSystemService(Context.AccessibilityService) is not AccessibilityManager manager)
        {
            return designMs;
        }

        return Math.Max(designMs, manager.GetRecommendedTimeoutMillis(designMs, ContentMode.Text));
    }
}
