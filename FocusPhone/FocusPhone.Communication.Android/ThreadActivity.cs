using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Time;
using FocusPhone.Domain.Timeline;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

/// <summary>
/// One contact's merged history with action buttons: call (Google Phone stays
/// the dialer), text (deep link into Google Messages until the embedded
/// bridge lands), and email (sent directly via Gmail).
/// </summary>
[Activity(
    Label = "Thread",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class ThreadActivity : ListActivity
{
    public const string ExtraContactKey = "contactKey";

    private string contactKey = "";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        contactKey = Intent?.GetStringExtra(ExtraContactKey) ?? "";
        Title = ContactNameResolver.DisplayName(this, contactKey);
        PaperUi.StylePaperList(this, ListView!);
        ListView!.AddFooterView(BuildActionRow());
        LoadItems();
    }

    private void LoadItems()
    {
        var items = new List<TimelineItem>();
        try
        {
            using var database = TimelineDatabase.Open(this);
            items.AddRange(database.Cache.LoadAll());
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Timeline cache unavailable: {e}");
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
        ListAdapter = new PaperListAdapter(this, (thread?.Items ?? [])
            .Select(item => new PaperListRow(
                item.Snippet,
                $"{(item.Incoming ? "←" : "→")} {RelativeTime.Format(item.TimestampUtc.ToLocalTime(), DateTime.Now)}"))
            .ToList());
    }

    private View BuildActionRow()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var horizontal = PaperTheme.Dp(this, DesignTokens.Space.X3);
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X4);
        row.SetPadding(horizontal, vertical, horizontal, vertical);

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
        var pad = PaperTheme.Dp(this, DesignTokens.Space.X4);
        layout.SetPadding(pad, pad, pad, 0);
        var subject = PaperUi.Field(this, "Subject");
        var body = PaperUi.Field(this, "Message");
        body.SetMinLines(4);
        layout.AddView(subject);
        var bodyParams = new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            TopMargin = PaperTheme.Dp(this, DesignTokens.Space.X3),
        };
        layout.AddView(body, bodyParams);

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
                    // Success is the state change: the sent mail appears in the
                    // thread on the next sync; nothing else to announce.
                    LoadItems();
                }
                catch (Exception e)
                {
                    Toast.MakeText(this, $"Send failed: {e.Message}. Check the app password in Settings.", ToastLength.Long)!.Show();
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void AddAction(LinearLayout row, string label, Action onClick)
    {
        var button = PaperUi.SecondaryButton(this, label, onClick);
        var layoutParams = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
        };
        row.AddView(button, layoutParams);
    }
}
