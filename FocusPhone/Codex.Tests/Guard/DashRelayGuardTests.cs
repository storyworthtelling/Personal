using System.Text.RegularExpressions;
using Watch.Domain;
using Xunit;

namespace Codex.Tests.Guard;

/// <summary>
/// The phone and the wrist answer by one set of rules (P14 W-4.1.1): the Mailbox and Post's relay to Dash both build
/// what is waiting, name a row, choose a reply's numbers and a text's route through the same pure types, so the watch
/// can never be shown a different Mailbox, or reply by a different road, than the phone. And Post names Dash's paths
/// only through <see cref="WatchIdentity"/>, so the two ends cannot drift.
///
/// <para>B2 adds Post's listener: it hears every Dash path under a frozen registered name, says where it is by a frozen
/// capability, carries each plan out through the code the Mailbox itself uses (one send road, one delete buffer, one
/// commit at the source), takes one message at a time, keeps its book across a restart, and waits for every Data Layer
/// call a bounded time.</para>
/// </summary>
public class DashRelayGuardTests
{
    private const string Post = "FocusPhone/Post.Android";

    private static string Code(string relative) => Regex.Replace(LogLoopSource.File(relative), @"///.*|//.*", string.Empty);

    private static IReadOnlyList<(string Path, string Text)> PostSources() =>
        LogLoopSource.Sources(Post).Select(source => (source.Path, Regex.Replace(source.Text, @"///.*|//.*", string.Empty))).ToList();

    private static string DashHalf() =>
        string.Join("\n", PostSources().Where(source => source.Path.StartsWith($"{Post}/Dash/", StringComparison.Ordinal)).Select(source => source.Text));

    [Theory]
    [InlineData("MailboxView.Build(")]
    [InlineData("ThreadTitles.ForRow(")]
    [InlineData("ReplyTarget.PhoneOf(")]
    [InlineData("ReplyTarget.GroupRecipients(")]
    [InlineData("ReplyTarget.MembershipKnown(")]
    [InlineData("ReplyTarget.ConversationOf(")]
    [InlineData("TextCourier.Deliver(")]
    [InlineData("TextCourier.DeliverMedia(")]
    [InlineData("SourceDeletes.Held")]
    [InlineData("SourceDeletes.Commit(")]
    [InlineData("MailboxSources.LiveTail(")]
    public void TheMailboxUsesTheSharedRules(string call)
    {
        Assert.Contains(call, Code($"{Post}/MailboxArea.cs"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("MailboxView.Build(")]
    [InlineData("ThreadTitles.ForRow(")]
    [InlineData("ReplyTarget.For(")]
    [InlineData("TextSend.Choose(")]
    [InlineData("TextSend.WithoutBridge(")]
    [InlineData("SyncMoment.Delay(PostAction.Filed")]
    [InlineData("SyncMoment.Delay(PostAction.Deleted")]
    [InlineData("CallNote.For(")]
    [InlineData("FoldTiming.WindowFor(")]
    public void TheRelayUsesTheSameRules(string call)
    {
        Assert.Contains(call, Code("FocusPhone/Codex.Infrastructure/Dash/DashRelay.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void TheMailboxNoLongerBuildsItsOwn()
    {
        // The reload's merge and the row title live in Codex.Domain now; a second copy here could drift from the wrist's.
        var mailbox = Code($"{Post}/MailboxArea.cs");

        Assert.DoesNotContain("thread.Participants.Take(3)", mailbox.Split("private string OpenThreadTitle")[0], StringComparison.Ordinal);
        Assert.DoesNotContain("recipients.Count > 1", mailbox, StringComparison.Ordinal);
        Assert.DoesNotContain("LastOrDefault(item => item.ExternalConversationId", mailbox, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMailboxNoLongerDeletesItself()
    {
        // The commit at the source and the delete buffer are shared with the listener, so a delete from the wrist is held
        // where the Mailbox holds its own, and carried out by the same code.
        var mailbox = Code($"{Post}/MailboxArea.cs");

        Assert.DoesNotContain("private void CommitDelete", mailbox, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteProviderRow(", mailbox, StringComparison.Ordinal);
        Assert.DoesNotContain("new DeleteBuffer()", mailbox, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ReadLiveTail", mailbox, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePhoneSendsTextsInOnePlace()
    {
        // One send road (bridge, then a plain text or one MMS): only the courier calls a route, and the courier runs the
        // pure cascade, which alone decides between them.
        string[] routes = ["SmsSender.Send(", "MmsSender.Send(", ".TrySend(", ".TrySendMedia(", "FindOrStartConversation(", "TextSend.Choose(", "TextSend.WithoutBridge("];
        string[] definitions = ["TextCourier.cs", "GmBridgeClient.cs", "SmsSender.cs", "MmsSender.cs"];

        var elsewhere = PostSources()
            .Where(source => !definitions.Contains(Path.GetFileName(source.Path)))
            .SelectMany(source => routes.Where(route => source.Text.Contains(route, StringComparison.Ordinal)).Select(route => $"{source.Path}: {route}"))
            .ToList();
        var courier = Code($"{Post}/TextCourier.cs");
        var cascade = Code("FocusPhone/Codex.Domain/Post/TextCascade.cs");

        Assert.True(elsewhere.Count == 0, "Send through TextCourier, the phone's one send road:\n" + string.Join("\n", elsewhere));
        Assert.Contains("TextCascade.Text(", courier, StringComparison.Ordinal);
        Assert.Contains("TextCascade.Media(", courier, StringComparison.Ordinal);
        Assert.Contains("TextSend.Choose(", cascade, StringComparison.Ordinal);
        Assert.Contains("TextSend.WithoutBridge(", cascade, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDashPathsAreNotCopied()
    {
        Assert.All(WatchIdentity.DashPaths, path => Assert.StartsWith("/dash/", path, StringComparison.Ordinal));
        string[] words = [.. WatchIdentity.DashPaths, WatchIdentity.DashListenerName, WatchIdentity.PostCapability, WatchIdentity.DashPreferences];
        Assert.All(words, word => Assert.False(string.IsNullOrEmpty(word)));

        var copies = new[] { Post, "FocusPhone/Codex.Infrastructure", "FocusPhone/Codex.Domain" }
            .SelectMany(LogLoopSource.Sources)
            .Where(source => words.Any(word => source.Text.Contains($"\"{word}", StringComparison.Ordinal)))
            .Select(source => source.Path)
            .ToList();

        Assert.True(copies.Count == 0, "Use WatchIdentity rather than a copy of a Dash path or name:\n" + string.Join("\n", copies));
    }

    // ---- Post's listener (B2) ----

    [Fact]
    public void PostRegistersItsListenerByName()
    {
        var listeners = PostSources().Where(source => source.Text.Contains("Name = WatchIdentity.DashListenerName", StringComparison.Ordinal)).ToList();

        var listener = Assert.Single(listeners).Text;
        Assert.Contains(": WearableListenerService", listener, StringComparison.Ordinal);
        Assert.Contains("MessageClient.ActionMessageReceived", listener, StringComparison.Ordinal);
        string[] paths =
        [
            "DashPeoplePath", "DashThreadPath", "DashSendPath", "DashFilePath", "DashDeletePath", "DashUndoPath", "DashReadPath",
            "DashCallNotePath", "DashMailPath", "DashPhotoPath", "DashRolodexPath",
        ];
        // B8: dash/arrived runs the other way, Post telling the wrist a text arrived, so Post's ear doesn't hear it.
        Assert.Equal(WatchIdentity.DashPaths.Count - 1, paths.Length);
        Assert.All(paths, path => Assert.Contains($"DataPath = WatchIdentity.{path}", listener, StringComparison.Ordinal));
        Assert.DoesNotContain("WatchIdentity.DashArrivedPath", listener, StringComparison.Ordinal);
    }

    [Fact]
    public void PostAdvertisesItsCapability()
    {
        // The watch finds the phone that carries the owner's texts by this capability, as Flyleaf finds the installer.
        var wear = LogLoopSource.File($"{Post}/Resources/values/wear.xml");

        Assert.Matches(@"<string-array\s+name=""android_wear_capabilities""", wear);
        Assert.Contains($"<item>{WatchIdentity.PostCapability}</item>", wear, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("DashInbox.Turn(")]
    [InlineData("DashInbox.Delivered(")]
    [InlineData("TextCourier.Deliver(")]
    [InlineData("Filing.File(")]
    [InlineData("Filing.Unfile(")]
    [InlineData("SourceDeletes.Held.Schedule(")]
    [InlineData("SourceDeletes.Held.Undo(")]
    [InlineData("SourceDeletes.Commit(")]
    [InlineData("ReadState.MarkRead(")]
    [InlineData("Cache.Upsert(")]
    [InlineData("Syncs.Later(")]
    [InlineData("MailboxSources.LiveTail(")]
    [InlineData("Cache.LoadPage(")]
    [InlineData("DogEar.UndoWindow(")]
    [InlineData("AttachmentCache.Bytes(")]
    [InlineData("DashInbox.Photo(")]
    [InlineData("PhotoFit.SampleFor(")]
    [InlineData("PhotoFit.Sizes(")]
    [InlineData("PhotoFit.TurnFor(")]
    [InlineData("PhotoFit.Qualities")]
    [InlineData("ContactPicker.People(")]
    [InlineData("new RolodexRelay(")]
    [InlineData("DashContacts.Rolodex(context)")]
    [InlineData("CallNote.Settles(")]
    [InlineData("PendingCallNoteStore.Clear(")]
    public void TheListenerCarriesThePlansThroughPostsOwnCode(string call)
    {
        Assert.Contains(call, DashHalf(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWristsRolodexIsThePhones()
    {
        // B6 (PC6): one address book, Post's. The wrist is shown the phone's own Rolodex, read as the Rolodex area reads it,
        // and a contact's numbers are put in one order for both, so a new text from the wrist reaches the number the phone's
        // Rolodex would text.
        var picker = Code($"{Post}/ContactPicker.cs");
        var rolodex = Code($"{Post}/RolodexArea.cs");

        Assert.Contains("RolodexNumbers.Order(", picker, StringComparison.Ordinal);
        Assert.Contains("new ContactNumber(value, primary, type)", picker, StringComparison.Ordinal);
        Assert.Contains("\"is_super_primary\", \"is_primary\", \"data2\"", picker, StringComparison.Ordinal);
        Assert.Contains("ContactPicker.People(Host)", rolodex, StringComparison.Ordinal);
        Assert.Contains("(\"Text\", Channel.Text, person.Phones[0])", rolodex, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWristIsShownNoNumberFromTheBook()
    {
        // The owner's ruling (B6): names and digest ids only. The head hands the relay what the picker read, and the relay
        // alone decides what crosses; nothing in Post's half writes a number or an address into a contact.
        var contacts = PostSources().Single(source => source.Path == $"{Post}/Dash/DashContacts.cs").Text;

        Assert.Contains("new RolodexPerson(", contacts, StringComparison.Ordinal);
        Assert.DoesNotContain("new DashContact(", DashHalf(), StringComparison.Ordinal);
        Assert.DoesNotContain("new DashRolodex(", DashHalf(), StringComparison.Ordinal);
        Assert.DoesNotContain("Emails", contacts, StringComparison.Ordinal);
    }

    [Fact]
    public void TheWristsPhotoIsFetchedAsTheMailboxOpensOne()
    {
        // B5: one fetch for a picture, whoever asks: through Google Messages, or from the phone's own MMS store.
        var cache = Code($"{Post}/AttachmentCache.cs");
        var fetch = Regex.Match(cache, @"public static Android\.Net\.Uri\? Fetch\([^)]*\)\s*\{(?<body>[\s\S]*?)\n    \}");

        Assert.True(fetch.Success, "AttachmentCache has no Fetch.");
        Assert.Contains("Bytes(context, item, attachment)", fetch.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("internal static byte[] Bytes(", cache, StringComparison.Ordinal);
    }

    [Fact]
    public void APhotoLeavesThePhoneShrunkAndReEncoded()
    {
        // Shrunk to fit one message and written again as a JPEG, so none of the picture's metadata reaches the watch, and
        // never written down on the phone on the way.
        var photos = PostSources().Single(source => source.Path == $"{Post}/Dash/DashPhotos.cs").Text;

        Assert.Contains("Bitmap.CompressFormat.Jpeg", photos, StringComparison.Ordinal);
        Assert.Contains("InJustDecodeBounds = true", photos, StringComparison.Ordinal);
        Assert.Contains("ExifInterface", photos, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAllBytes", photos, StringComparison.Ordinal);
        Assert.DoesNotContain("CacheDir", photos, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryDataLayerWaitIsBounded()
    {
        // As Flyleaf's watch half (release A fix round, finding 4): a Data Layer call that never answers holds nothing.
        string[] calls = ["SendMessageAsync(", "GetCapabilityAsync(", "OpenChannelAsync(", "GetOutputStreamAsync(", "CloseAsync("];
        var lines = PostSources()
            .SelectMany(source => source.Text.Split('\n').Select((line, index) => (source.Path, Line: line, Number: index + 1)))
            .Where(entry => calls.Any(call => entry.Line.Contains(call, StringComparison.Ordinal)))
            .ToList();

        Assert.NotEmpty(lines);
        var unbounded = lines.Where(entry => !entry.Line.Contains("BoundedAsync(", StringComparison.Ordinal)).Select(entry => $"{entry.Path}:{entry.Number}").ToList();
        Assert.True(unbounded.Count == 0, "A Data Layer call waited for with no bound:\n" + string.Join("\n", unbounded));
        Assert.Contains("deadline.Cut(DateTimeOffset.UtcNow, DashReplies.ReplyWait)", DashHalf(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnAnswerIsSentOnceAndAMissIsCounted()
    {
        // The watch-unreachable rule: one attempt on the node that asked, no queue, no retry, and a count line.
        var sends = PostSources().Sum(source => Regex.Matches(source.Text, @"SendMessageAsync\(").Count);
        var half = DashHalf();

        Assert.Equal(1, sends);
        Assert.Contains(".Missed()", half, StringComparison.Ordinal);
        Assert.Contains(".Landed()", half, StringComparison.Ordinal);
        Assert.Contains("DashOutcome.Unreached", half, StringComparison.Ordinal);
        Assert.Contains("SourceNodeId", half, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerTakesOneMessageAtATime()
    {
        var half = DashHalf();

        Assert.Matches(@"static readonly UpdateRunGate \w+ = new\(\);", half);
        Assert.Contains(".TryRunAsync(", half, StringComparison.Ordinal);
        Assert.Contains("DashReplies.GateWait", half, StringComparison.Ordinal);
        Assert.Contains("DashOutcome.Busy", half, StringComparison.Ordinal);
    }

    [Fact]
    public void TheListenerWorksOffTheMainThread()
    {
        var listener = Regex.Match(DashHalf(), @"public override void OnMessageReceived\(IMessageEvent \w+\)\s*\{(?<body>[\s\S]*?)\n    \}");

        Assert.True(listener.Success, "The listener has no OnMessageReceived.");
        Assert.Contains("Task.Run(", listener.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("Looper.MainLooper", listener.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Contains("DashReplies.TurnWait", listener.Groups["body"].Value, StringComparison.Ordinal);
    }

    [Fact]
    public void TheActsSurviveARestart()
    {
        var half = DashHalf();

        Assert.Contains("WatchIdentity.DashPreferences", half, StringComparison.Ordinal);
        Assert.Contains("WatchIdentity.DashActsKey", half, StringComparison.Ordinal);
        Assert.Contains("DashActsCodec.Encode(", half, StringComparison.Ordinal);
        Assert.Contains("DashActsCodec.Decode(", half, StringComparison.Ordinal);
        Assert.Contains(".Owed()", half, StringComparison.Ordinal);
        Assert.Contains(".Settle(", half, StringComparison.Ordinal);
        Assert.Contains("DashHolds.Resume(", Code($"{Post}/PostApplication.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void PostReadsTheWatchsCodec()
    {
        var project = LogLoopSource.File($"{Post}/Post.Android.csproj");

        Assert.Contains(@"..\..\Watch\Watch.Domain\Watch.Domain.csproj", project, StringComparison.Ordinal);
        Assert.Contains("<PackageReference Include=\"Xamarin.GooglePlayServices.Wearable\" />", project, StringComparison.Ordinal);
        Assert.Contains("DashCodec.Decode(", DashHalf(), StringComparison.Ordinal);
        Assert.Contains("DashCodec.Encode(", DashHalf(), StringComparison.Ordinal);
    }

    // ---- B8: a text arriving, told to the wrist so it can buzz (WD §7, A12) ----

    [Fact]
    public void ATextIsToldToTheWristWherePostAlertsForIt()
    {
        // Where the phone itself alerts for an arriving text, and only there: a spam text never reaches Post's alert, so it
        // never reaches the wrist either. Both of Post's ways in (the Messages notification and a text it received itself)
        // go through the one alert.
        var notifications = Code($"{Post}/SmsNotifications.cs");
        var alert = Regex.Match(notifications, @"private static void Post\([^)]*\)\s*\{(?<body>[\s\S]*?)\n    \}");

        Assert.True(alert.Success, "SmsNotifications has no Post.");
        Assert.Contains("WristArrivals.Tell(context, ", alert.Groups["body"].Value, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(notifications, @"\bPost\(context, ").Count);
        var spam = notifications.IndexOf("if (IsSpam(context, latest))", StringComparison.Ordinal);
        var told = notifications.IndexOf("Post(context, alert.ThreadKey", StringComparison.Ordinal);
        Assert.True(spam >= 0 && told > spam, "A text is told to the wrist before it is judged spam.");
    }

    [Fact]
    public void TheWristIsToldOnceWhileItIsInReach()
    {
        // The watch-unreachable rule (B2) still holds: the wrist is found by its capability among the nodes in reach, told
        // once on the pool with every wait bounded, and nothing is queued or tried again.
        var arrivals = PostSources().Single(source => source.Path == $"{Post}/Dash/WristArrivals.cs").Text;

        foreach (var call in new[]
        {
            "WatchIdentity.DashCapability", "CapabilityClient.FilterReachable", "RelayNodes.Choose(", "DashArrivals.For(", "Task.Run(",
            "DashCarrier.SendOnceAsync(", "DashStars.IsStarred(",
        })
        {
            Assert.Contains(call, arrivals, StringComparison.Ordinal);
        }
        foreach (var queue in new[] { "while", "for (", "Retry", "Enqueue", "WorkManager", "Later(" })
        {
            Assert.DoesNotContain(queue, arrivals, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AFavouriteIsStarredInThePhonesContacts()
    {
        // A12: favourites are the ones starred in the Rolodex, which is the phone's own address book.
        var stars = PostSources().Single(source => source.Path == $"{Post}/Dash/DashStars.cs").Text;

        Assert.Contains("ContactsContract.PhoneLookup.ContentFilterUri", stars, StringComparison.Ordinal);
        Assert.Contains("ContactsContract.PhoneLookup.InterfaceConsts.Starred", stars, StringComparison.Ordinal);
    }

    [Fact]
    public void PostAsksForTheWristByItsCapability()
    {
        // Dash says where it is by a capability of its own, as Post does for the wrist.
        var wear = LogLoopSource.File("Watch/Dash.Wear/Resources/values/wear.xml");

        Assert.Matches(@"<string-array\s+name=""android_wear_capabilities""", wear);
        Assert.Contains($"<item>{WatchIdentity.DashCapability}</item>", wear, StringComparison.Ordinal);
    }
}
