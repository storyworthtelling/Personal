using Android.App;
using Android.Content;
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
            if (account.IsConfigured)
            {
                // Sync now so conversations appear promptly instead of waiting
                // for the next periodic run.
                EmailSyncWorker.RunOnce(this);
            }
            Toast.MakeText(this, "Saved.", ToastLength.Short)!.Show();
            Finish();
        };

        var enableMessages = new Button(this) { Text = "Enable message sync (notification access)" };
        enableMessages.Click += (_, _) =>
            StartActivity(new Intent(Android.Provider.Settings.ActionNotificationListenerSettings));

        var note = new TextView(this)
        {
            Text = "Message sync reads incoming texts from Google Messages notifications and " +
                   "voicemails (with transcription) from Google Phone notifications. " +
                   "Call and text history require the call-log and SMS permissions, which take " +
                   "effect after the device is locked down (or a whitelisted install).",
            TextSize = 13,
        };
        note.SetTextColor(Android.Graphics.Color.ParseColor("#8A8578"));

        layout.AddView(address);
        layout.AddView(password);
        layout.AddView(save, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 48 });
        layout.AddView(enableMessages, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 24 });
        layout.AddView(note, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent) { TopMargin = 32 });
        SetContentView(layout);
    }
}
