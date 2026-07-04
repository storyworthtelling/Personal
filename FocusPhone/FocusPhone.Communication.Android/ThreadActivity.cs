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
        try
        {
            items.AddRange(SmsLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
        }

        var thread = new TimelineMerger().Merge(items)
            .FirstOrDefault(t => t.ContactKey == contactKey);
        var lines = (thread?.Items ?? [])
            .Select(item => $"{(item.Incoming ? "←" : "→")} {item.TimestampUtc.ToLocalTime():MMM d HH:mm}  {TimelineItemFormatter.Describe(item)}")
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
            AddAction(row, "Email", () =>
                EmailComposeDialog.Show(this, contactKey, Title?.ToString() ?? contactKey));
        }
        return row;
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
