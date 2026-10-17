using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using Constitution.Domain;

namespace Constitution;

/// <summary>
/// Constitution's launcher activity. For now it opens a neutral page and nothing else; the five
/// tabs arrive with their releases.
///
/// <para>Its Java name is registered explicitly from <see cref="ConstitutionIdentity.MainActivityName"/>:
/// the home-screen entry points at it, so a rename of this class or namespace cannot move it
/// (guarded by <c>ConstitutionInvariantsTests</c>).</para>
/// </summary>
[Activity(
    Name = ConstitutionIdentity.MainActivityName,
    Label = "Constitution",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var page = new TextView(this) { Text = "Constitution" };
        page.SetPadding(48, 96, 48, 48);
        SetContentView(page);
    }
}
