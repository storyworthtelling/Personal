using Android.App;
using Android.Content;
using Android.OS;
using Pulp.Domain.Core;

namespace Pulp.Settings;

/// <summary>
/// A service's own page (D272, no mockup): its plan, attestation and visibility. The entry point exists before the page so the Release D screens can be built in parallel
/// without sharing a file.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class ServiceActivity : Activity
{
    public const string ExtraServiceId = "serviceId";

    public static Intent For(Context context, ServiceId service)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(ServiceActivity));
        intent.PutExtra(ExtraServiceId, service.Value);
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
