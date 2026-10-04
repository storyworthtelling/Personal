using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Ui;
using Pulp.Domain;

namespace Pulp;

/// <summary>
/// Pulp's launch activity — for now only the Paper &amp; Ink page with its Display header. The
/// screens arrive in Release C.
///
/// <para>Its Java name is registered explicitly from <see cref="PulpIdentity.MainActivityName"/>
/// so a later rename of this class or namespace cannot move it (guarded by
/// <c>PulpInvariantsTests</c>).</para>
///
/// <para>No launcher icon yet: IconStudio generates it later in Release A, so no
/// <c>Icon</c> is named here and the platform default stands until then.</para>
/// </summary>
[Activity(
    Name = PulpIdentity.MainActivityName,
    Label = "Pulp",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var page = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Space.X6);
        page.SetPadding(edge, edge, edge, edge);
        page.AddView(PaperUi.PageHeader(this, "Pulp"), new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        SetContentView(PaperUi.WrapForTopInset(this, page));
    }
}
