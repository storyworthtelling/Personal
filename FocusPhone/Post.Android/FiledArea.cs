using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Ui;

namespace Post;

/// <summary>
/// Filed: correspondence that has been dealt with — deleted at the source, kept here as a
/// ghost. The other half of the to-do-list frame the Mailbox is the front of.
///
/// The filing verb itself is not built yet, so this area is honestly empty rather than
/// absent: the tab is here from the start so its position never moves under the owner's
/// hand once filing lands (§9's reason for a top-aligned strip). Per the owner's note on
/// the mock-ups, there is no explanatory preamble at the top — they know how it works.
/// </summary>
public sealed class FiledArea(Activity host) : PostArea(host)
{
    public override BinderArea Tab =>
        new("Filed", Resource.Drawable.area_filed, PaperTheme.CautionMark, PaperTheme.CautionFill);

    public override View BuildView()
    {
        var frame = new FrameLayout(Host);
        frame.AddView(PaperUi.EmptyState(Host, "Nothing filed."), new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return frame;
    }
}
