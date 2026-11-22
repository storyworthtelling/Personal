using Codex.Domain.Post;
using Codex.Domain.Timeline;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// What the Mailbox shows, built in one place for the phone and the watch (P14 B1): the reload's own steps, unchanged,
/// taken out of <c>MailboxArea</c> so Post's relay to Dash cannot drift from it.
/// </summary>
public class MailboxViewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 21, 16, 20, 0, TimeSpan.Zero);

    private static TimelineItem Text(string who, int minute, string words = "hi", string? id = null) =>
        new(TimelineItemKind.Message, who, T0.AddMinutes(minute), words, Incoming: true, SourceId: id ?? $"sms:{who}:{minute}");

    private static TimelineItem Mail(string from, int minute, MailClass? kind) =>
        new(TimelineItemKind.Email, from, T0.AddMinutes(minute), "words", Incoming: true, SourceId: $"mail:{from}:{minute}")
        {
            Subject = "Saturday plans",
            MailClass = kind,
        };

    private static MailboxView Build(IEnumerable<TimelineItem> items,
        IReadOnlySet<(TimelineItemKind, string)>? hidden = null, SpamList? spam = null, DeleteBuffer? deletes = null,
        IReadOnlyDictionary<string, DateTimeOffset>? read = null) =>
        MailboxView.Build(items, hidden ?? new HashSet<(TimelineItemKind, string)>(), spam ?? SpamList.From([]), deletes ?? new DeleteBuffer(),
            PerKeyIdentityResolver.Instance, new ReadState(read ?? new Dictionary<string, DateTimeOffset>()));

    [Fact]
    public void ItIsTheMailboxsOwnMerge()
    {
        TimelineItem[] items = [Text("2125550101", 0), Text("2125550102", 5), Text("2125550101", 10)];

        var view = Build(items);

        Assert.Equal(new TimelineMerger().Merge(items).Select(thread => thread.ContactKey), view.Waiting.Select(thread => thread.ContactKey));
        Assert.Equal(2, view.Waiting[0].Items.Count);
    }

    [Fact]
    public void HiddenSpamAndPendingDeletesAreNotShown()
    {
        var hidden = Text("2125550101", 0);
        var spam = Text("2125550102", 1, "You have won a cruise, reply YES to claim it now");
        var pending = Text("2125550103", 2);
        var kept = Text("2125550104", 3);
        var deletes = new DeleteBuffer();
        deletes.Schedule(pending, T0, TimeSpan.FromSeconds(6));
        var spamList = SpamList.From([]);
        spamList.Mark([spam]);

        var view = Build([hidden, spam, pending, kept], hidden: new HashSet<(TimelineItemKind, string)> { (hidden.Kind, hidden.SourceId) },
            spam: spamList, deletes: deletes);

        Assert.Equal(["2125550104"], view.Waiting.Select(thread => thread.ContactKey));
    }

    [Fact]
    public void MarketingHasItsOwnSection()
    {
        var view = Build([Mail("sam@example.com", 0, MailClass.Human), Mail("deals@example.com", 1, MailClass.Marketing), Mail("news@example.com", 2, null)]);

        Assert.Equal(["news@example.com", "sam@example.com"], view.Waiting.Select(thread => thread.ContactKey));
        Assert.Equal(["deals@example.com"], view.Marketing.Select(thread => thread.ContactKey));
    }

    [Fact]
    public void UnreadIsStampedFromTheReadRecord()
    {
        var view = Build([Text("2125550101", 0), Text("2125550102", 5)],
            read: new Dictionary<string, DateTimeOffset> { ["2125550101"] = T0.AddMinutes(1) });

        Assert.False(view.Waiting.Single(thread => thread.ContactKey == "2125550101").HasUnread);
        Assert.True(view.Waiting.Single(thread => thread.ContactKey == "2125550102").HasUnread);
    }
}
