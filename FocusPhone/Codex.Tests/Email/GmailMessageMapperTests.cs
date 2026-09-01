using Codex.Domain.Timeline;
using Codex.Infrastructure.Email;
using MimeKit;
using Xunit;

namespace Codex.Tests.Email;

public class GmailMessageMapperTests
{
    private const string AccountAddress = "me@gmail.com";

    private static readonly GmailMessageMapper Mapper = new(AccountAddress);

    private static MimeMessage Message(
        string from, string to, string subject = "Hello",
        string? messageId = "<abc@mail.gmail.com>", string body = "Body text")
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = subject;
        message.Date = new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero);
        if (messageId is not null)
        {
            message.MessageId = messageId.Trim('<', '>');
        }
        else
        {
            // MimeKit auto-generates a Message-Id; drop it to exercise the fallback.
            message.Headers.Remove(HeaderId.MessageId);
        }
        message.Body = new TextPart("plain") { Text = body };
        return message;
    }

    [Fact]
    public void IncomingMessage_KeysOnSender()
    {
        var item = Mapper.Map(Message(from: "Alice@Example.com", to: AccountAddress));

        Assert.True(item.Incoming);
        Assert.Equal("alice@example.com", item.ContactKey);
    }

    [Fact]
    public void OutgoingMessage_KeysOnRecipient()
    {
        var item = Mapper.Map(Message(from: AccountAddress, to: "bob@example.com"));

        Assert.False(item.Incoming);
        Assert.Equal("bob@example.com", item.ContactKey);
    }

    [Fact]
    public void Map_ProducesEmailItemWithSubjectSnippetAndUtcTimestamp()
    {
        var item = Mapper.Map(Message("alice@example.com", AccountAddress, subject: "Lunch?"));

        Assert.Equal(TimelineItemKind.Email, item.Kind);
        Assert.Equal("Lunch?", item.Snippet);
        Assert.Equal(new DateTimeOffset(2026, 6, 10, 9, 30, 0, TimeSpan.Zero), item.TimestampUtc);
    }

    [Fact]
    public void Map_EmptySubject_FallsBackToBodyFirstLine()
    {
        var item = Mapper.Map(Message("alice@example.com", AccountAddress,
            subject: "", body: "First line\nSecond line"));

        Assert.Equal("First line", item.Snippet);
    }

    [Fact]
    public void Map_UsesMessageIdAsSourceId()
    {
        var item = Mapper.Map(Message("alice@example.com", AccountAddress));

        Assert.Equal("abc@mail.gmail.com", item.SourceId);
    }

    [Fact]
    public void Map_SameMessage_ProducesSameSourceIdWithoutMessageId()
    {
        var first = Mapper.Map(Message("alice@example.com", AccountAddress, messageId: null));
        var second = Mapper.Map(Message("alice@example.com", AccountAddress, messageId: null));

        Assert.Equal(first.SourceId, second.SourceId);
    }

    [Fact]
    public void Map_PopulatesEmailHeaderFieldsForThreadingAndReply()
    {
        var message = Message("alice@example.com", AccountAddress, subject: "Lunch?", body: "Body text");
        message.Cc.Add(MailboxAddress.Parse("carol@example.com"));
        message.InReplyTo = "prev@mail.gmail.com";

        var item = Mapper.Map(message);

        Assert.Equal("Lunch?", item.Subject);
        Assert.Equal("Body text", item.Body);
        Assert.Equal("alice@example.com", item.FromAddress);
        Assert.Equal([AccountAddress], item.Recipients);
        Assert.Equal(["carol@example.com"], item.CarbonCopy);
        Assert.Equal("abc@mail.gmail.com", item.MessageId);
        Assert.Equal("prev@mail.gmail.com", item.InReplyTo);
    }
}
