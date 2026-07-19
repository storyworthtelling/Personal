using Android.App;
using Android.Content;

namespace FocusPhone.Communication;

/// <summary>
/// Launches the in-app email composer (EmailComposeActivity). Kept as a small
/// helper so the thread action row, the reply action, and the new-conversation
/// flow all open compose the same way. Optional reply prefill (subject +
/// In-Reply-To) threads the reply.
/// </summary>
public static class EmailComposeDialog
{
    public static void Show(
        Activity activity, string toAddress, string displayName,
        string? prefillSubject = null, string? inReplyTo = null)
    {
        var intent = new Intent(activity, typeof(EmailComposeActivity));
        intent.PutExtra(EmailComposeActivity.ExtraTo, toAddress);
        intent.PutExtra(EmailComposeActivity.ExtraName, displayName);
        if (prefillSubject is not null)
        {
            intent.PutExtra(EmailComposeActivity.ExtraSubject, prefillSubject);
        }
        if (inReplyTo is not null)
        {
            intent.PutExtra(EmailComposeActivity.ExtraInReplyTo, inReplyTo);
        }
        activity.StartActivity(intent);
    }
}
