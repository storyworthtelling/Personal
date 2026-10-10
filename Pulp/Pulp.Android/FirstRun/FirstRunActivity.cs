using Android.App;
using Android.Content;
using Android.OS;

namespace Pulp.FirstRun;

/// <summary>
/// First run (D142): restore the vault, set up the household, say whose phone this is. The entry point exists before the page so the Release D screens can be built in parallel
/// without sharing a file.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class FirstRunActivity : Activity
{
    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(FirstRunActivity));
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
