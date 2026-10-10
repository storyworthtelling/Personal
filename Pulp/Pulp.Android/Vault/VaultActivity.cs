using Android.App;
using Android.Content;
using Android.OS;

namespace Pulp.Vault;

/// <summary>
/// The Vault (D142, D272): the passphrase that unlocks Pulp's keys, and each key's health. The entry point exists before the page so the Release D screens can be built in parallel
/// without sharing a file.
/// </summary>
[Activity(Exported = false, Theme = "@style/Theme.FocusPhone.Paper")]
public class VaultActivity : Activity
{
    public static Intent For(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var intent = new Intent(context, typeof(VaultActivity));
        return intent;
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Finish();
    }
}
