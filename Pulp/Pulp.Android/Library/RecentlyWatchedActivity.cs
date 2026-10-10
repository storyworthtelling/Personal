using Android.App;
using Android.Content;
using Android.OS;

namespace Pulp.Library;

/// <summary>
/// Recently watched (mockup 05, D226-D229), reached from the Watching page's bottom bar (D213). The
/// entry point exists before the page so the screens of Release C wave 2 can be built in parallel.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class RecentlyWatchedActivity : Activity
{
    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new Intent(context, typeof(RecentlyWatchedActivity));
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
