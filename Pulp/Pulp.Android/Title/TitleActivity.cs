using Android.App;
using Android.Content;
using Android.OS;
using Pulp.Domain.Core;

namespace Pulp.Title;

/// <summary>
/// A title's page (mockup 02, D218; a film's page, D272). Every screen that lists a title opens it
/// through <see cref="For"/>, so the entry point exists before the page does: Release C wave 2's
/// screens are built in parallel and each needs somewhere to send a tap.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class TitleActivity : Activity
{
    public const string ExtraTitleId = "titleId";

    public static Intent For(Context context, TitleId title)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(TitleActivity));
        intent.PutExtra(ExtraTitleId, title.Value);
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
