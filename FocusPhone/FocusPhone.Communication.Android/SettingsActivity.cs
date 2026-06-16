using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;

namespace FocusPhone.Communication;

/// <summary>
/// Gmail account setup: address + app password. Reached by long-pressing the
/// thread list; on a locked-down phone this is where admin mode rotates the
/// app password.
/// </summary>
[Activity(
    Label = "Settings",
    Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class SettingsActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var account = new AccountStore(this);

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetBackgroundColor(ThreadsActivity.Paper);
        layout.SetPadding(48, 96, 48, 48);

        var address = new EditText(this) { Hint = "Gmail address", Text = account.GmailAddress };
        var password = new EditText(this)
        {
            Hint = "App password (myaccount.google.com/apppasswords)",
            Text = account.GmailAppPassword,
            InputType = Android.Text.InputTypes.ClassText | Android.Text.InputTypes.TextVariationPassword,
        };
        var save = new Button(this) { Text = "Save" };
        save.Click += (_, _) =>
        {
            account.GmailAddress = address.Text;
            account.GmailAppPassword = password.Text;
            Toast.MakeText(this, "Saved.", ToastLength.Short)!.Show();
            Finish();
        };

        layout.AddView(address);
        layout.AddView(password);
        layout.AddView(save, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 48 });
        SetContentView(layout);
    }
}
