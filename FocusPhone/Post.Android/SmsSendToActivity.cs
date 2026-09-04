using Android.App;
using Android.Content;
using Android.OS;

namespace Post;

/// <summary>
/// The fourth component the SMS role requires: something that answers SENDTO for the
/// sms/smsto/mms/mmsto schemes, so "text this person" from anywhere else on the phone
/// lands in Post.
///
/// It is a router rather than a screen — it turns the URI into a contact key, opens that
/// correspondence, and finishes, so Back never returns to an empty shim.
/// </summary>
[Activity(
    Name = "com.storyworthtelling.focusphone.communication.SmsSendToActivity",
    Label = "Post",
    Exported = true,
    NoHistory = true,
    Theme = "@style/Theme.FocusPhone.Paper")]
[IntentFilter(
    [Intent.ActionSendto, Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataSchemes = ["sms", "smsto", "mms", "mmsto"])]
public class SmsSendToActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var number = Intent?.Data?.SchemeSpecificPart;
        if (!string.IsNullOrWhiteSpace(number))
        {
            var thread = new Intent(this, typeof(ThreadActivity));
            thread.PutExtra(ThreadActivity.ExtraContactKey, Android.Net.Uri.Decode(number));
            StartActivity(thread);
        }
        else
        {
            // No recipient in the URI: open the app rather than a dead end.
            StartActivity(new Intent(this, typeof(PostActivity)));
        }

        Finish();
    }
}
