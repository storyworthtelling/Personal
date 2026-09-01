using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// What one Claude call is asked to bring back about a piece of mail: what kind it is, and
/// any date it proposes. Both in one call because detection costs nothing extra once the
/// message is already being read, and two calls for one message would be two bills.
///
/// The reply is a model's, so every shape it can come back in has to be survivable: a
/// missing field, an unknown kind, a date that is not a date. None of those may take the
/// sync down, because the sync is also how mail arrives at all.
/// </summary>
public class MailReadingTests
{
    [Fact]
    public void AWellFormedReadingIsUnderstood()
    {
        var reading = MailReading.Parse(
            """{"kind":"newsletter","event":{"title":"Recital","startsOn":"2026-09-12","startsAt":"19:30"}}""");

        Assert.Equal(MailClass.Newsletter, reading.Kind);
        Assert.Equal("Recital", reading.Event!.Title);
        Assert.Equal(new DateOnly(2026, 9, 12), reading.Event.StartsOn);
        Assert.Equal(new TimeOnly(19, 30), reading.Event.StartsAt);
    }

    [Theory]
    [InlineData("human", MailClass.Human)]
    [InlineData("marketing", MailClass.Marketing)]
    [InlineData("newsletter", MailClass.Newsletter)]
    [InlineData("bill", MailClass.Bill)]
    [InlineData("transactional", MailClass.Transactional)]
    [InlineData("HUMAN", MailClass.Human)]
    public void EveryKindTheModelIsOfferedIsUnderstood(string kind, MailClass expected)
    {
        Assert.Equal(expected, MailReading.Parse($$"""{"kind":"{{kind}}"}""").Kind);
    }

    // A kind nobody offered it means the model improvised. The safe answer is "no idea"
    // rather than a guess: an invented kind could age a real message out in a day.
    [Theory]
    [InlineData("""{"kind":"spam"}""")]
    [InlineData("""{"kind":""}""")]
    [InlineData("""{}""")]
    public void AnUnknownKindIsNoAnswerAtAll(string json)
    {
        Assert.Null(MailReading.Parse(json).Kind);
    }

    // Mail with no date in it is the common case, and must not produce a phantom event —
    // a date detected that was never there raises a fold on a thread about nothing.
    [Fact]
    public void MailWithNoDateProposesNoEvent()
    {
        Assert.Null(MailReading.Parse("""{"kind":"human"}""").Event);
    }

    // A model's reply is not a contract. Nothing here may throw, because this runs inside
    // the sync — and a sync that dies on a malformed reply stops mail arriving at all.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("{\"kind\":")]
    [InlineData("[]")]
    [InlineData("null")]
    public void AGarbledReplyIsSurvivable(string json)
    {
        var reading = MailReading.Parse(json);

        Assert.Null(reading.Kind);
        Assert.Null(reading.Event);
    }

    // Models like to wrap JSON in prose or a fence however firmly they are told not to.
    [Theory]
    [InlineData("""Here you go: {"kind":"bill"}""")]
    [InlineData("```json\n{\"kind\":\"bill\"}\n```")]
    public void JsonIsFoundInsideProseOrAFence(string reply)
    {
        Assert.Equal(MailClass.Bill, MailReading.Parse(reply).Kind);
    }

    // An event with no usable date is not an event. Half a date is worse than none: it
    // would put a dog-ear on a thread and then have nothing to propose.
    [Theory]
    [InlineData("""{"kind":"human","event":{"title":"Recital","startsOn":"soon"}}""")]
    [InlineData("""{"kind":"human","event":{"title":"Recital"}}""")]
    [InlineData("""{"kind":"human","event":{"startsOn":"2026-09-12"}}""")]
    public void AnEventMissingItsDateOrTitleIsDropped(string json)
    {
        Assert.Null(MailReading.Parse(json).Event);
    }

    // A date without a time is a real thing — an all-day event — and must survive.
    [Fact]
    public void AnAllDayEventKeepsItsDateAndHasNoTime()
    {
        var reading = MailReading.Parse(
            """{"kind":"human","event":{"title":"Sports day","startsOn":"2026-09-12"}}""");

        Assert.Equal(new DateOnly(2026, 9, 12), reading.Event!.StartsOn);
        Assert.Null(reading.Event.StartsAt);
    }
}
