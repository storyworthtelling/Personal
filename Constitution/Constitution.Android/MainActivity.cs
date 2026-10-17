using Android.App;
using Android.Content;
using Constitution.Domain;

// The launcher icon IconStudio generates from the approved painting (spec "Constitution",
// resource name appicon, which build-constitution.yml's ICON CHECK reads back out of the APK).
[assembly: Application(Icon = "@mipmap/appicon", RoundIcon = "@mipmap/appicon_round", Label = "Constitution")]

namespace Constitution;

/// <summary>
/// Constitution's launcher activity, which IS the shell (<see cref="ConstitutionActivity"/>): the
/// five-tab binder frame. It stays a class of its own because its Java name is registered
/// explicitly from <see cref="ConstitutionIdentity.MainActivityName"/>: the home-screen entry
/// points at it, so a rename of this class or namespace cannot move it (guarded by
/// <c>ConstitutionInvariantsTests</c>).
/// </summary>
[Activity(
    Name = ConstitutionIdentity.MainActivityName,
    Label = "Constitution",
    MainLauncher = true,
    Exported = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter([Intent.ActionMain], Categories = [Intent.CategoryLauncher])]
public class MainActivity : ConstitutionActivity
{
}
