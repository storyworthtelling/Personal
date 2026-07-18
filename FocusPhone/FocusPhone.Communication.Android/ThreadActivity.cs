using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using FocusPhone.Domain.Design;
using FocusPhone.Domain.Time;
using FocusPhone.Domain.Timeline;
using FocusPhone.Infrastructure.Email;
using FocusPhone.Ui;

namespace FocusPhone.Communication;

/// <summary>
/// One person's or group's unified history — texts, calls, voicemails, and email
/// interleaved by time, each row channel-labelled. A pinned action bar offers the
/// channels that person has (call/text/email); opening the thread marks it read.
/// Long-pressing an item offers call-back / text-back and an undo-first delete
/// (email → Gmail trash, call/voicemail → call log, text → hidden locally until
/// the bridge can remote-delete). Texting still hands off to Google Messages
/// until the in-app bridge lands.
/// </summary>
[Activity(
    Label = "Thread",
    Exported = false,
    Theme = "@style/Theme.FocusPhone.Paper")]
public class ThreadActivity : Activity
{
    public const string ExtraContactKey = "contactKey";

    private readonly DeleteBuffer deleteBuffer = new();
    private string contactKey = "";
    private ListView? listView;
    private FrameLayout? undoHost;
    private LinearLayout? actionRow;
    private TextView? titleView;
    private Handler? handler;
    private List<TimelineItem> items = [];
    private IReadOnlyList<string> memberKeys = [];
    private bool isGroup;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        PaperTransitions.Apply(this);
        handler = new Handler(Looper.MainLooper!);
        contactKey = Intent?.GetStringExtra(ExtraContactKey) ?? "";
        SetContentView(BuildLayout());

        listView!.ItemLongClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < items.Count)
            {
                ShowItemActions(items[e.Position]);
            }
        };

        LoadItems();
    }

    private View BuildLayout()
    {
        undoHost = new FrameLayout(this);

        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var edge = PaperTheme.Dp(this, DesignTokens.Shape.ScreenEdgeDp);
        column.SetPadding(edge, PaperTheme.Dp(this, DesignTokens.Space.X8), edge, 0);

        titleView = PaperUi.PageHeader(this, "");
        column.AddView(titleView);

        listView = new ListView(this);
        PaperUi.StylePaperList(this, listView);
        column.AddView(listView, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, 0) { Weight = 1 });

        actionRow = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        var vertical = PaperTheme.Dp(this, DesignTokens.Space.X4);
        actionRow.SetPadding(0, vertical, 0, vertical);
        column.AddView(actionRow, new LinearLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));

        undoHost.AddView(column, new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
        return undoHost;
    }

    private void LoadItems()
    {
        var gathered = new List<TimelineItem>();
        try
        {
            gathered.AddRange(CallLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
        }
        try
        {
            gathered.AddRange(SmsLogReader.ReadAll(this));
        }
        catch (Java.Lang.SecurityException)
        {
        }

        ContactThread? thread = null;
        try
        {
            using var database = TimelineDatabase.Open(this);
            gathered.AddRange(database.Cache.LoadAll());

            var visible = database.Hidden.Filter(gathered);
            var resolver = ContactAssociations.BuildResolver(this, database.IdentityLinks);
            thread = new TimelineMerger().Merge(visible, resolver)
                .FirstOrDefault(candidate => candidate.ContactKey == contactKey);

            if (thread is not null)
            {
                // Opening a thread is seeing it: stamp read so it drops out of the
                // inbox's unread section on return.
                database.ReadState.MarkRead(thread.ContactKey, DateTimeOffset.Now);
            }
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Timeline stores unavailable: {e}");
            thread = new TimelineMerger().Merge(gathered)
                .FirstOrDefault(candidate => candidate.ContactKey == contactKey);
        }

        memberKeys = thread?.MemberKeys ?? [];
        isGroup = thread?.IsGroup ?? false;
        titleView!.Text = Title(thread);

        // Hide anything the owner just deleted (still inside its undo window).
        items = deleteBuffer.Filter(thread?.Items ?? []).ToList();
        listView!.Adapter = new PaperListAdapter(this, items
            .Select(item => new PaperListRow(
                TimelineItemFormatter.Describe(item),
                $"{(item.Incoming ? "←" : "→")} {RelativeTime.Format(item.TimestampUtc.LocalDateTime, DateTime.Now)}"))
            .ToList());

        BuildActionRow();
    }

    private string Title(ContactThread? thread)
    {
        if (thread is null)
        {
            return ContactNameResolver.DisplayName(this, contactKey);
        }
        if (thread.IsGroup)
        {
            var names = thread.Participants.Take(3).Select(key => ContactNameResolver.DisplayName(this, key));
            var joined = string.Join(", ", names);
            return joined.Length == 0 ? "Group" : joined;
        }
        return ContactNameResolver.DisplayName(this, thread.ContactKey);
    }

    private void BuildActionRow()
    {
        actionRow!.RemoveAllViews();
        if (isGroup)
        {
            return; // Group replies need the bridge; no per-channel deep link yet.
        }

        var phone = memberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(contactKey) ? contactKey : null);
        var email = memberKeys.FirstOrDefault(IsEmail) ?? (IsEmail(contactKey) ? contactKey : null);

        if (phone is not null)
        {
            AddAction("Call", () => PlaceCall(phone));
            AddAction("Text", () => SendText(phone));
        }
        if (email is not null)
        {
            AddAction("Email", () => EmailComposeDialog.Show(this, email, titleView!.Text!.ToString()!));
        }
    }

    private void AddAction(string label, Action onClick)
    {
        var button = PaperUi.SecondaryButton(this, label, onClick);
        actionRow!.AddView(button, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent)
        {
            Weight = 1,
            MarginEnd = PaperTheme.Dp(this, DesignTokens.Space.X3),
        });
    }

    // Long-press: contextual actions per item — call-back / text-back for a person
    // with a number, plus the undo-first delete.
    private void ShowItemActions(TimelineItem item)
    {
        var labels = new List<string>();
        var actions = new List<Action>();

        var phone = memberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(contactKey) ? contactKey : null);
        if (phone is not null && !isGroup)
        {
            labels.Add("Call back");
            actions.Add(() => PlaceCall(phone));
            labels.Add("Text back");
            actions.Add(() => SendText(phone));
        }
        labels.Add("Delete");
        actions.Add(() => DeleteWithUndo(item));

        new AlertDialog.Builder(this)
            .SetItems(labels.ToArray(), (_, e) => actions[e.Which]())!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void DeleteWithUndo(TimelineItem item)
    {
        var window = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);
        deleteBuffer.Schedule(item, DateTimeOffset.Now, window);
        LoadItems(); // Item disappears immediately.

        UndoBar.Show(this, undoHost!, $"Deleted {ChannelWord(item)}.", onUndo: () =>
        {
            deleteBuffer.Undo(item.Kind, item.SourceId);
            LoadItems();
        });

        handler!.PostDelayed(() =>
        {
            // Commit only if it was not undone in the meantime.
            if (deleteBuffer.Undo(item.Kind, item.SourceId))
            {
                CommitDelete(item);
            }
        }, DesignTokens.Motion.UndoWindowMs);
    }

    // Performs the irreversible, channel-specific delete once the undo window closes.
    private void CommitDelete(TimelineItem item)
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            try
            {
                switch (item.Kind)
                {
                    case TimelineItemKind.Email:
                        DeleteEmail(item);
                        break;
                    case TimelineItemKind.Call:
                    case TimelineItemKind.Voicemail:
                        DeleteCallOrVoicemail(item);
                        break;
                    default:
                        // Text: the SMS provider only lets the default SMS app delete, and
                        // bridge remote-delete isn't wired yet — hide it locally for now.
                        HideLocally(item);
                        break;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Delete of {item.Kind}:{item.SourceId} failed: {e}");
            }
        });
    }

    private void DeleteEmail(TimelineItem item)
    {
        var account = new AccountStore(this);
        if (account.IsConfigured && item.MessageId is not null)
        {
            new GmailClient(account.GmailAddress!, account.GmailAppPassword!)
                .DeleteAsync(item.MessageId).GetAwaiter().GetResult();
        }
        using var database = TimelineDatabase.Open(this);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void DeleteCallOrVoicemail(TimelineItem item)
    {
        if (item.SourceId.All(char.IsAsciiDigit))
        {
            // A live call-log row: remove it at the source (needs WRITE_CALL_LOG).
            // "_id" is the stable BaseColumns row-id name for the call-log provider.
            ContentResolver!.Delete(Android.Provider.CallLog.Calls.ContentUri!,
                "_id = ?", [item.SourceId]);
        }
        using var database = TimelineDatabase.Open(this);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void HideLocally(TimelineItem item)
    {
        using var database = TimelineDatabase.Open(this);
        database.Hidden.Hide(item.Kind, item.SourceId);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    private void PlaceCall(string number) =>
        StartActivity(new Intent(Intent.ActionCall, Android.Net.Uri.Parse($"tel:{number}")));

    // Deep-link to Google Messages until the in-app bridge lands (documented fallback).
    private void SendText(string number) =>
        StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{number}")));

    private static string ChannelWord(TimelineItem item) => item.Kind switch
    {
        TimelineItemKind.Email => "email",
        TimelineItemKind.Call => "call",
        TimelineItemKind.Voicemail => "voicemail",
        _ => "text",
    };

    private static bool IsPhone(string key) => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static bool IsEmail(string key) => key.Contains('@');
}
