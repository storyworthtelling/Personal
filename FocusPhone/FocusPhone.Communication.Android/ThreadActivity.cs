using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Timeline;

namespace FocusPhone.Communication;

/// <summary>
/// One contact's merged history with action buttons: call (Google Phone stays
/// the dialer), text (deep link into Google Messages until the embedded
/// bridge lands), and email (sent directly via Gmail).
/// </summary>
[Activity(
    Label = "Thread",
    Exported = false,
    Theme = "@android:style/Theme.Material.Light.NoActionBar")]
public class ThreadActivity : ListActivity
{
    public const string ExtraContactKey = "contactKey";

    private string contactKey = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        contactKey = Intent?.GetStringExtra(ExtraContactKey) ?? "";
        Title = ContactNameResolver.DisplayName(this, contactKey);
        ListView!.SetBackgroundColor(ThreadsActivity.Paper);
        ListView.AddFooterView(BuildActionRow());
        LoadItems();
    }

    private void LoadItems()
    {
        List<TimelineItem> items;
        using (var database = TimelineDatabase.Open(this))
        {
            items = database.Cache.LoadAll().ToList();
        }
        try
        {
            items.AddRange(CallLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
        }

        var thread = new TimelineMerger().Merge(items)
            .FirstOrDefault(t => t.ContactKey == contactKey);
        var lines = (thread?.Items ?? [])
            .Select(item => $"{(item.Incoming ? "←" : "→")} {item.TimestampUtc.ToLocalTime():MMM d HH:mm}  {item.Snippet}")
            .ToList();
        ListAdapter = new ArrayAdapter<string>(this, Android.Resource.Layout.SimpleListItem1, lines);
    }

    private View BuildActionRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(16, 24, 16, 24);

        var isPhone = contactKey.All(char.IsAsciiDigit);
        var isEmail = contactKey.Contains('@');

        if (isPhone)
        {
            AddAction(row, "Call", () =>
                StartActivity(new Intent(Intent.ActionCall, Android.Net.Uri.Parse($"tel:{contactKey}"))));
            AddAction(row, "Text", () =>
                StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{contactKey}"))));
        }
        if (isEmail)
        {
            AddAction(row, "Email", ShowComposeDialog);
        }
        return row;
    }

    private void ShowComposeDialog()
    {
        var account = new AccountStore(this);
        if (!account.IsConfigured)
        {
            Toast.MakeText(this, "Set up Gmail in settings first (long-press the thread list).",
                ToastLength.Long)!.Show();
            return;
        }

        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var subject = new EditText(this) { Hint = "Subject" };
        var body = new EditText(this) { Hint = "Message" };
        body.SetMinLines(4);
        layout.AddView(subject);
        layout.AddView(body);

        new AlertDialog.Builder(this)
            .SetTitle($"Email {Title}")!
            .SetView(layout)!
            .SetPositiveButton("Send", async (_, _) =>
            {
                try
                {
                    var client = new Infrastructure.Email.GmailClient(
                        account.GmailAddress!, account.GmailAppPassword!);
                    await client.SendAsync(contactKey, subject.Text ?? "", body.Text ?? "");
                    Toast.MakeText(this, "Sent.", ToastLength.Short)!.Show();
                }
                catch (Exception e)
                {
                    Toast.MakeText(this, $"Send failed: {e.Message}", ToastLength.Long)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void AddAction(LinearLayout row, string label, Action onClick)
    {
        var button = new Button(this) { Text = label };
        button.Click += (_, _) => onClick();
        var layoutParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = 16,
        };
        row.AddView(button, layoutParams);
    }
}
