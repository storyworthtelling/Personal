using Android.App;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain.Shell;

namespace Pulp.Shell;

/// <summary>
/// Settings, one tab of Pulp's binder (mockup 01, D212). Services, the household and the children
/// are mockup 09. Until then the page shows its header, one quiet status sentence from <see
/// cref="PulpAreas"/>, and the build line every Settings screen in the suite ends with.
/// </summary>
public sealed class SettingsArea(Activity host) : PulpAreaPage(host, PulpArea.Settings)
{
    public override View BuildView()
    {
        var column = (LinearLayout)base.BuildView();

        // The quiet foot line: which build is actually on the phone. The channel installs
        // silently and most updates change nothing visible, so without it "did that land?" has
        // no answer on the device.
        var lp = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent);
        lp.TopMargin = PaperTheme.Dp(Host, DesignTokens.Space.X8);
        column.AddView(PaperUi.BuildNumber(Host, "Pulp"), lp);
        return column;
    }
}
