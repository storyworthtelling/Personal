using Android.App;
using Android.Widget;

namespace FocusPhone.Communication;

/// <summary>
/// In-app email composer: subject + body sent directly via Gmail (no external
/// mail app). Shared by the thread action row and the new-conversation flow.
/// </summary>
public static class EmailComposeDialog
{
    public static void Show(Activity activity, string toAddress, string displayName)
    {
        var account = new AccountStore(activity);
        if (!account.IsConfigured)
        {
            Toast.MakeText(activity, "Set up Gmail in Settings first.", ToastLength.Long)!.Show();
            return;
        }

        var layout = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        var subject = new EditText(activity) { Hint = "Subject" };
        var body = new EditText(activity) { Hint = "Message" };
        body.SetMinLines(4);
        layout.AddView(subject);
        layout.AddView(body);

        new AlertDialog.Builder(activity)
            .SetTitle($"Email {displayName}")!
            .SetView(layout)!
            .SetPositiveButton("Send", async (_, _) =>
            {
                try
                {
                    var client = new Infrastructure.Email.GmailClient(
                        account.GmailAddress!, account.GmailAppPassword!);
                    await client.SendAsync(toAddress, subject.Text ?? "", body.Text ?? "");
                    Toast.MakeText(activity, "Sent.", ToastLength.Short)!.Show();
                }
                catch (Exception e)
                {
                    Toast.MakeText(activity, $"Send failed: {e.Message}", ToastLength.Long)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }
}
