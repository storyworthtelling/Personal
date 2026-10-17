using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Ui;

namespace Constitution;

/// <summary>
/// The Settings tab. Plain by decision (D71): no crinkle and no scraps, ever. A neutral page
/// for now; its content arrives with its release. It has no action yet, so it has no pinned bar.
/// It ends with the build line, because there is no adb after the cutover and that line is the
/// only way to answer "did that update land?" on the device.
/// </summary>
public sealed class SettingsArea(Activity host)
    : ConstitutionAreaBase(host, "settings", Resource.Drawable.area_settings)
{
    protected override View BuildPage()
    {
        var column = new LinearLayout(Host) { Orientation = Orientation.Vertical };
        column.AddView(PageFurniture.Sentence(Host, "Nothing to set yet."));
        column.AddView(PaperUi.BuildNumber(Host, "Constitution"));
        return column;
    }
}
