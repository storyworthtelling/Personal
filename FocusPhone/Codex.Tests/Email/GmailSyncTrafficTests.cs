using System.Net;
using Codex.Infrastructure.Email;
using Xunit;

namespace Codex.Tests.Email;

// Efficiency audit §2: every file, delete or Sync tap re-downloaded up to 200 whole raw messages, attachments and
// all, one at a time, only to learn which still existed. A reconcile now lists ids and fetches only the ones this
// phone has never seen; the 30-minute sync asks Gmail's history what was added; and a message comes down without
// its attachments' bytes, which are fetched when opened.
public class GmailSyncTrafficTests
{
    private const string Base = "https://gmail.googleapis.com/gmail/v1/users/me";
    private const string WindowList = $"{Base}/messages?q=%28in%3Ainbox%20OR%20in%3Asent%29%20after%3A1756461600&maxResults=200";
    private static readonly DateTimeOffset Since = DateTimeOffset.FromUnixTimeSeconds(1756461600);

    private static GmailApiClient Client(FakeHttpMessageHandler handler) =>
        new("owner@example.com", () => Task.FromResult("token-123"), new HttpClient(handler));

    private static int Fetches(FakeHttpMessageHandler handler) =>
        handler.Requests.Count(request => request.RequestUri!.AbsolutePath.Contains("/messages/g", StringComparison.Ordinal));

    [Fact]
    public async Task AReconcileFetchesOnlyMessagesThisPhoneHasNotSeen()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/profile", """{"historyId":"500"}""");
        handler.Respond(WindowList, """{"messages":[{"id":"g1"},{"id":"g2"}]}""");
        handler.Respond($"{Base}/messages/g2?format=full",
            GmailPayloads.Plain("g2", "her@example.com", "owner@example.com", "Dinner", "Seven?", "<m2@example.com>"));
        var index = new MemoryGmailIdIndex();
        index.Remember("g1", "<m1@example.com>");

        var mirror = await Client(handler).MirrorAsync(Since, index);

        Assert.Equal(1, Fetches(handler));
        Assert.Equal("Dinner", Assert.Single(mirror.Fetched).Subject);
        Assert.Equal(["<m1@example.com>", "m2@example.com"], mirror.Present.Order(StringComparer.Ordinal));
        Assert.Equal("m2@example.com", index.SourceIdFor("g2"));
        Assert.Equal("500", mirror.HistoryId);
    }

    [Fact]
    public async Task ASecondReconcileWithNothingNewFetchesNothing()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/profile", """{"historyId":"500"}""");
        handler.Respond(WindowList, """{"messages":[{"id":"g2"}]}""");
        handler.Respond($"{Base}/messages/g2?format=full",
            GmailPayloads.Plain("g2", "her@example.com", "owner@example.com", "Dinner", "Seven?", "<m2@example.com>"));
        var index = new MemoryGmailIdIndex();
        await Client(handler).MirrorAsync(Since, index);
        handler.Requests.Clear();

        var again = await Client(handler).MirrorAsync(Since, index);

        Assert.Equal(0, Fetches(handler));
        Assert.Empty(again.Fetched);
        Assert.Equal(["m2@example.com"], again.Present);
    }

    [Fact]
    public async Task TheHistoryNamesWhatWasAddedToTheInboxOrSent()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/history?startHistoryId=400&historyTypes=messageAdded",
            """
            {"historyId":"520","history":[
              {"messagesAdded":[{"message":{"id":"g3","labelIds":["INBOX","UNREAD"]}}]},
              {"messagesAdded":[{"message":{"id":"g4","labelIds":["DRAFT"]}}]},
              {"messagesAdded":[{"message":{"id":"g5","labelIds":["SENT"]}}]}]}
            """);
        handler.Respond($"{Base}/messages/g3?format=full",
            GmailPayloads.Plain("g3", "her@example.com", "owner@example.com", "Lunch", "Noon?", "<m3@example.com>"));
        handler.Respond($"{Base}/messages/g5?format=full",
            GmailPayloads.Plain("g5", "owner@example.com", "her@example.com", "Re: Lunch", "Yes", "<m5@example.com>"));

        var changes = await Client(handler).ChangesSinceAsync("400", new MemoryGmailIdIndex());

        Assert.NotNull(changes);
        Assert.Equal(["Lunch", "Re: Lunch"], changes.Fetched.Select(item => item.Subject).Order(StringComparer.Ordinal));
        Assert.Equal("520", changes.HistoryId);
    }

    [Fact]
    public async Task TheHistoryIsReadPageByPage()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/history?startHistoryId=400&historyTypes=messageAdded",
            """{"historyId":"510","nextPageToken":"p2","history":[{"messagesAdded":[{"message":{"id":"g3","labelIds":["INBOX"]}}]}]}""");
        handler.Respond($"{Base}/history?startHistoryId=400&historyTypes=messageAdded&pageToken=p2",
            """{"historyId":"520","history":[{"messagesAdded":[{"message":{"id":"g5","labelIds":["INBOX"]}}]}]}""");
        var index = new MemoryGmailIdIndex();
        index.Remember("g3", "<m3@example.com>");
        index.Remember("g5", "<m5@example.com>");

        var changes = await Client(handler).ChangesSinceAsync("400", index);

        Assert.Empty(changes!.Fetched);
        Assert.Equal("520", changes.HistoryId);
    }

    // Gmail keeps history for about a week; past that it answers 404 and the caller lists the window instead.
    [Fact]
    public async Task AnExpiredHistoryIsNullNotAFailure()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/history?startHistoryId=1&historyTypes=messageAdded", "{}", status: HttpStatusCode.NotFound);

        Assert.Null(await Client(handler).ChangesSinceAsync("1", new MemoryGmailIdIndex()));
    }

    [Fact]
    public async Task AnAttachmentIsDescribedWithoutItsBytes()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/profile", """{"historyId":"500"}""");
        handler.Respond(WindowList, """{"messages":[{"id":"g7"}]}""");
        handler.Respond($"{Base}/messages/g7?format=full", GmailPayloads.WithAttachment("g7", "Attached.", "quote.pdf", 123456));

        var item = Assert.Single((await Client(handler).MirrorAsync(Since, new MemoryGmailIdIndex())).Fetched);

        Assert.Equal("Attached.", item.Body?.Trim());
        Assert.Equal("a1@example.com", item.SourceId);
        var attachment = Assert.Single(item.Attachments);
        Assert.Equal("quote.pdf", attachment.FileName);
        Assert.Equal("application/pdf", attachment.MimeType);
        Assert.Equal(123456, attachment.SizeBytes);
        Assert.DoesNotContain(handler.Requests, request => request.RequestUri!.AbsolutePath.Contains("/attachments/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnHtmlOnlyMessageStillReads()
    {
        var handler = new FakeHttpMessageHandler();
        handler.Respond($"{Base}/profile", """{"historyId":"500"}""");
        handler.Respond(WindowList, """{"messages":[{"id":"g8"}]}""");
        handler.Respond($"{Base}/messages/g8?format=full", GmailPayloads.Plain(
            "g8", "her@example.com", "owner@example.com", "Dinner", "<html><body><p>Seven &amp; a half?</p></body></html>",
            "<m8@example.com>", "text/html; charset=UTF-8"));

        var item = Assert.Single((await Client(handler).MirrorAsync(Since, new MemoryGmailIdIndex())).Fetched);

        Assert.Equal("Seven & a half?", item.Body);
    }

    private sealed class MemoryGmailIdIndex : IGmailIdIndex
    {
        private readonly Dictionary<string, string> ids = new(StringComparer.Ordinal);

        public string? SourceIdFor(string gmailId) => ids.GetValueOrDefault(gmailId);

        public void Remember(string gmailId, string sourceId) => ids[gmailId] = sourceId;
    }
}
