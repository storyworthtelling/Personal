using Android.App;
using Android.Content;
using Android.OS;

namespace Pulp.Settings;

/// <summary>
/// Household (D272, no mockup): the people in it, the children's rules, and whose phone this is. The entry point exists before the page so the Release D screens can be built in parallel
/// without sharing a file.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class HouseholdActivity : Activity
{
    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(HouseholdActivity));
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
