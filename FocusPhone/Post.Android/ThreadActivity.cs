using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using Codex.Domain.Design;
using Codex.Domain.Time;
using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using Codex.Ui;
using Codex.Ui.Voice;

namespace Post;

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
    private string? bridgeConversationId;
    private const int PickBridgePhoto = 71;
    private string? pendingPhotoConversationId;
    private string? pendingPhotoNumber;

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

    // Gathering the live logs and merging the cache is I/O-heavy, so it runs off
    // the UI thread; only the bind touches views. Refreshes (send, delete, undo)
    // call this and let the list update a moment later.
    private void LoadItems()
    {
        System.Threading.Tasks.Task.Run(() =>
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
                PostDiagnostics.LogWarning("Timeline", "Timeline stores unavailable; using plain merge", e);
                thread = new TimelineMerger().Merge(gathered)
                    .FirstOrDefault(candidate => candidate.ContactKey == contactKey);
            }

            var loaded = thread;
            RunOnUiThread(() => BindThread(loaded));
        });
    }

    private void BindThread(ContactThread? thread)
    {
        memberKeys = thread?.MemberKeys ?? [];
        isGroup = thread?.IsGroup ?? false;
        titleView!.Text = Title(thread);

        // Hide anything the owner just deleted (still inside its undo window).
        items = deleteBuffer.Filter(thread?.Items ?? []).ToList();
        // The conversation id to reply through the bridge (learned from the most
        // recent bridge-captured message in this thread).
        bridgeConversationId = items.LastOrDefault(item => item.ExternalConversationId is not null)?.ExternalConversationId;
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

    // Long-press: what can be done to this one message. The shared action card rather than
    // a dialog — §7 makes the card the app's only overflow, and it is never a confirm
    // step: everything here does something, and the destructive things are undone from the
    // corner rather than asked about first.
    private void ShowItemActions(TimelineItem item)
    {
        var actions = new List<CardAction>();

        var phone = memberKeys.FirstOrDefault(IsPhone) ?? (IsPhone(contactKey) ? contactKey : null);
        if (phone is not null && !isGroup)
        {
            actions.Add(new CardAction("Call back", () => PlaceCall(phone)));
            actions.Add(new CardAction("Text back", () => SendText(phone), Lead: true));
        }

        var email = memberKeys.FirstOrDefault(IsEmail) ?? (IsEmail(contactKey) ? contactKey : null);
        if (item.Kind == TimelineItemKind.Email && email is not null)
        {
            actions.Add(new CardAction("Reply", () => EmailComposeDialog.Show(
                this, email, titleView!.Text!.ToString()!, ReplySubject(item.Subject), item.MessageId),
                Lead: true));
        }

        if (item.Kind == TimelineItemKind.Call && phone is not null)
        {
            actions.Add(new CardAction("Write a note on this call", () => WriteCallNote(phone)));
        }

        actions.Add(new CardAction("File", () => FileWithUndo(item)));
        actions.Add(new CardAction("Delete", () => DeleteWithUndo(item), Destructive: true));

        if (phone is not null && !isGroup)
        {
            actions.Add(new CardAction("Block this number", () => Block(phone), Destructive: true));
        }

        ActionCard.Show(this, undoHost!, titleView!.Text!.ToString()!,
            TimelineItemFormatter.Describe(item), actions.ToArray());
    }

    // The owner's own note on a call, since a transcript is not deliverable on Android.
    // It asks for a summary and nothing else — no lead-in explaining why, because the
    // owner already knows why and reads it every time.
    private void WriteCallNote(string number)
    {
        var input = PaperUi.Field(this, "A summary, for future reference");
        new AlertDialog.Builder(this)
            .SetTitle("Note on this call")!
            .SetView(input)!
            .SetPositiveButton("Keep", (_, _) =>
            {
                var text = (input.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    return;
                }
                using var database = TimelineDatabase.Open(this);
                database.Cache.Upsert([CallNote.For(number, text, DateTimeOffset.UtcNow)]);
                LoadItems();
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Blocking is handed to Android's own blocked-numbers list rather than kept here, so
    // it applies everywhere on the phone instead of only inside Post — a number blocked in
    // one app and ringing through in another is worse than not blocking it.
    private void Block(string number)
    {
        try
        {
            var values = new Android.Content.ContentValues();
            values.Put(Android.Provider.BlockedNumberContract.BlockedNumbers.ColumnOriginalNumber, number);
            ContentResolver!.Insert(
                Android.Provider.BlockedNumberContract.BlockedNumbers.ContentUri!, values);

            DogEar.Show(this, undoHost!, $"Blocked {number}.", onUndo: () => Unblock(number));
        }
        catch (Exception e)
        {
            // Only the default SMS or dialer app may write the blocked list. Post holds the
            // SMS role, so this should work — but if it does not, say so rather than
            // leaving the owner believing a number is blocked when it is not.
            Android.Util.Log.Warn("Post", $"Could not block {number}: {e}");
            PostDiagnostics.LogWarning("Block", $"Could not block {number}", e);
            DogEar.SetNotice(this, undoHost!, NoticeKeys.Block, new DogEar.Notice(
                "That number was not blocked.",
                "The phone would not accept it. Blocking is the system's list, not Post's, and it "
                + "refused — so calls from this number will still come through.",
                PaperTheme.ProblemMark));
        }
    }

    private void Unblock(string number)
    {
        try
        {
            ContentResolver!.Delete(
                Android.Provider.BlockedNumberContract.BlockedNumbers.ContentUri!,
                $"{Android.Provider.BlockedNumberContract.BlockedNumbers.ColumnOriginalNumber} = ?",
                [number]);
        }
        catch (Exception e)
        {
            PostDiagnostics.LogWarning("Block", $"Could not unblock {number}", e);
        }
    }

    // Filing one message: the ghost is kept and the source delete is owed. The correspondence
    // screen's unit is the message; the Mailbox's is the whole correspondence.
    private void FileWithUndo(TimelineItem item)
    {
        Filing.File(this, item, DateTimeOffset.UtcNow);
        LoadItems();

        DogEar.Show(this, undoHost!, $"Filed {ChannelWord(item)}.", onUndo: () =>
        {
            Filing.Unfile(this, item.SourceId);
            LoadItems();
        });
    }

    private void DeleteWithUndo(TimelineItem item)
    {
        var window = TimeSpan.FromMilliseconds(DesignTokens.Motion.UndoWindowMs);
        deleteBuffer.Schedule(item, DateTimeOffset.Now, window);
        LoadItems(); // Item disappears immediately.

        DogEar.Show(this, undoHost!, $"Deleted {ChannelWord(item)}.", onUndo: () =>
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
                        DeleteText(item);
                        break;
                }
            }
            catch (Exception e)
            {
                Android.Util.Log.Warn("FocusPhoneComm", $"Delete of {item.Kind}:{item.SourceId} failed: {e}");
                PostDiagnostics.LogWarning("Delete", $"Delete of {item.Kind}:{item.SourceId} failed", e);
            }
        });
    }

    private void DeleteEmail(TimelineItem item)
    {
        var account = new AccountStore(this);
        if (item.MessageId is not null)
        {
            switch (MailRoute.Choose(VoiceGoogleAccount.IsSignedIn, account.IsConfigured))
            {
                case MailTransport.GmailApi:
                    // A real trash, recoverable for 30 days — the same delete the Gmail app
                    // performs, rather than IMAP's move into a folder that happens to be named
                    // Trash. This is what makes filing safe enough to be a first-class verb.
                    MailAccess.Api(this, account).TrashAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
                case MailTransport.AppPassword:
                    new GmailClient(account.GmailAddress!, account.GmailAppPassword!)
                        .DeleteAsync(item.MessageId).GetAwaiter().GetResult();
                    break;
            }
        }
        using var database = TimelineDatabase.Open(this);
        database.Cache.Delete(item.Kind, item.SourceId);
    }

    // Deleting a text for real depends on where it came from, and the routing is decided
    // by pure tested logic rather than here. Holding the SMS role is what unlocks the
    // provider: only the default app may write to it, which is exactly why Post takes it.
    private void DeleteText(TimelineItem item)
    {
        var holdsRole = SmsRoleAccess.Status(this) == Codex.Domain.Post.SmsRoleStatus.Held;
        switch (TextDelete.Choose(item.SourceId, holdsRole, GmBridgeClient.Shared(this).IsConnected))
        {
            case TextDeletePath.SmsProvider:
                DeleteProviderRow(Android.Provider.Telephony.Sms.ContentUri!, item, "sms:");
                return;
            case TextDeletePath.MmsProvider:
                DeleteProviderRow(Android.Provider.Telephony.Mms.ContentUri!, item, "mms:");
                return;
            case TextDeletePath.Bridge:
                var messageId = item.SourceId["gm:".Length..];
                if (!messageId.Contains(':') && GmBridgeClient.Shared(this).TryDelete(messageId))
                {
                    using var database = TimelineDatabase.Open(this);
                    database.Cache.Delete(item.Kind, item.SourceId);
                    return;
                }
                break;
        }

        HideLocally(item);
    }

    // Removes the row the timeline item came from. The provider is the phone's own record
    // of the owner's texts, so this is the delete that actually means something — every
    // other app that reads it sees the message gone too.
    private void DeleteProviderRow(Android.Net.Uri contentUri, TimelineItem item, string prefix)
    {
        var rowId = item.SourceId[prefix.Length..];
        ContentResolver!.Delete(contentUri, "_id = ?", [rowId]);
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

    // The bridge first (the only route that carries RCS and full-quality pictures), then
    // Post sending the SMS itself as role holder, then — only if neither is possible —
    // handing the owner to Google Messages. Leaving Post to answer someone is the failure
    // this app is measured against, so the deep link is genuinely last.
    private void SendText(string number)
    {
        var holdsRole = SmsRoleAccess.Status(this) == Codex.Domain.Post.SmsRoleStatus.Held;
        switch (TextSend.Choose(
            GmBridgeClient.Shared(this).IsConnected, bridgeConversationId is not null, holdsRole))
        {
            case TextSendPath.Bridge:
                ShowBridgeCompose(bridgeConversationId!, number);
                return;
            case TextSendPath.NativeSms:
                ShowNativeCompose(number);
                return;
            default:
                StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{number}")));
                return;
        }
    }

    // Post sends the text itself. As role holder it is also responsible for the record, so
    // the sent message is written into the provider — Android does not do it for us, and a
    // sent text missing from the phone's own store is a text the owner cannot find later.
    private void ShowNativeCompose(string number)
    {
        var input = new EditText(this) { Hint = "Message" };
        new AlertDialog.Builder(this)
            .SetTitle("Text")!
            .SetView(input)!
            .SetPositiveButton("Send", (_, _) =>
            {
                var text = input.Text ?? string.Empty;
                if (text.Length == 0)
                {
                    return;
                }
                try
                {
                    SmsSender.Send(this, number, text);
                    LoadItems();
                }
                catch (Exception e)
                {
                    Android.Util.Log.Warn("Post", $"Native SMS send failed: {e}");
                    PostDiagnostics.LogWarning("Sms", "Sending a text failed", e);
                    DogEar.ShowNotices(this, undoHost!,
                    [
                        new DogEar.Notice("That text did not go out.",
                            "Post holds the texting role but the send failed. It is still in the box below.",
                            PaperTheme.ProblemMark),
                    ]);
                }
            })!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    private void ShowBridgeCompose(string conversationId, string number)
    {
        var input = new EditText(this) { Hint = "Message" };
        new AlertDialog.Builder(this)
            .SetTitle("Text")!
            .SetView(input)!
            .SetPositiveButton("Send", (_, _) =>
            {
                var text = input.Text ?? "";
                if (text.Length == 0)
                {
                    return;
                }
                if (GmBridgeClient.Shared(this).TrySend(conversationId, text))
                {
                    EchoOutgoing(conversationId, number, text);
                    LoadItems();
                }
                else
                {
                    Toast.MakeText(this, "Couldn't send — opening Messages.", ToastLength.Short)!.Show();
                    StartActivity(new Intent(Intent.ActionSendto, Android.Net.Uri.Parse($"smsto:{number}")));
                }
            })!
            .SetNeutralButton("Photo", (_, _) => PickPhotoForBridge(conversationId, number))!
            .SetNegativeButton("Cancel", (_, _) => { })!
            .Show();
    }

    // Picture text: pick an image and send it through the bridge; the result
    // arrives in OnActivityResult (the dialog is already gone by then).
    private void PickPhotoForBridge(string conversationId, string number)
    {
        pendingPhotoConversationId = conversationId;
        pendingPhotoNumber = number;
        var intent = new Intent(Intent.ActionGetContent);
        intent.SetType("image/*");
        intent.AddCategory(Intent.CategoryOpenable);
        StartActivityForResult(Intent.CreateChooser(intent, "Photo"), PickBridgePhoto);
    }

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != PickBridgePhoto || resultCode != Result.Ok || data?.Data is null
            || pendingPhotoConversationId is null)
        {
            return;
        }

        var conversationId = pendingPhotoConversationId;
        var number = pendingPhotoNumber ?? "";
        var mime = ContentResolver!.GetType(data.Data) ?? "image/jpeg";
        var bytes = ReadBytes(data.Data);
        pendingPhotoConversationId = null;
        pendingPhotoNumber = null;
        if (bytes is null)
        {
            return;
        }

        System.Threading.Tasks.Task.Run(() =>
        {
            if (GmBridgeClient.Shared(this).TrySendMedia(conversationId, bytes, "photo", mime))
            {
                RunOnUiThread(() =>
                {
                    EchoOutgoing(conversationId, number, "📷 Photo");
                    LoadItems();
                });
            }
            else
            {
                RunOnUiThread(() => Toast.MakeText(this, "Couldn't send photo.", ToastLength.Short)!.Show());
            }
        });
    }

    private byte[]? ReadBytes(Android.Net.Uri uri)
    {
        try
        {
            using var input = ContentResolver!.OpenInputStream(uri);
            if (input is null)
            {
                return null;
            }
            using var memory = new System.IO.MemoryStream();
            input.CopyTo(memory);
            return memory.ToArray();
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Reading photo failed: {e}");
            PostDiagnostics.LogWarning("Photo", "Reading photo attachment failed", e);
            return null;
        }
    }

    // The bridge only reports incoming messages, so record the sent one locally
    // (keyed to the person) to reflect it in the thread immediately.
    private void EchoOutgoing(string conversationId, string number, string text)
    {
        try
        {
            using var database = TimelineDatabase.Open(this);
            database.Cache.Upsert([new TimelineItem(
                TimelineItemKind.Message,
                ContactKeyNormalizer.NormalizePhone(number),
                DateTimeOffset.UtcNow, text, Incoming: false,
                SourceId: $"gmout:{conversationId}:{DateTimeOffset.UtcNow.Ticks}")
            {
                ExternalConversationId = conversationId,
            }]);
        }
        catch (Exception e)
        {
            Android.Util.Log.Warn("FocusPhoneComm", $"Outgoing echo failed: {e}");
            PostDiagnostics.LogWarning("Bridge", "Recording sent message locally failed", e);
        }
    }

    private static string ReplySubject(string? subject)
    {
        var trimmed = (subject ?? "").Trim();
        if (trimmed.Length == 0)
        {
            return "Re:";
        }
        return trimmed.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? trimmed : $"Re: {trimmed}";
    }

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
