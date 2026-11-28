using Watch.Domain;
using Watch.Domain.Dash;
using static Watch.Tests.Dash.DashFixtures;

namespace Watch.Tests.Dash;

/// <summary>
/// How the wrist knows which of Post's answers is the one it asked for (W-5.1.1, W-5.1.2): Post answers on the path it
/// was asked on, once (B2), so People waiting is answered by people, a conversation by its own key, a send by its own id;
/// a read is told and answered by nothing. Every wait is bounded, and a send waits out Post's whole turn.
/// </summary>
public class DashExchangeTests
{
    [Fact]
    public void AnAnswerFindsItsAsk()
    {
        Assert.Equal(DashExchange.Awaits(new DashPeopleAsk()), DashExchange.Answers(People));
        Assert.Equal(DashExchange.Awaits(new DashPeopleAsk()), DashExchange.Answers(new DashPeople([])));
        Assert.Equal(DashExchange.Awaits(new DashMailAsk()), DashExchange.Answers(Messages["mail"]));
        Assert.NotNull(DashExchange.Awaits(new DashPeopleAsk()));
    }

    [Fact]
    public void AConversationIsAnsweredOnlyForItsKey()
    {
        var asked = DashExchange.Awaits(new DashThreadAsk(GroupKey));

        Assert.Equal(asked, DashExchange.Answers(GroupThread));
        Assert.NotEqual(asked, DashExchange.Answers(Messages["person's thread"]));
        Assert.NotEqual(asked, DashExchange.Answers(People));
    }

    [Fact]
    public void ASendIsAnsweredByItsId()
    {
        var asked = DashExchange.Awaits(new DashSend("send-1", LeeKey, [], "On my way."));

        Assert.Equal(asked, DashExchange.Answers(new DashSent("send-1", DashSendOutcome.Failed)));
        Assert.NotEqual(asked, DashExchange.Answers(new DashSent("send-2", DashSendOutcome.Sent)));
    }

    [Fact]
    public void APhotoIsAnsweredOnlyForItsId()
    {
        // A conversation asks for each of its photos on its own (W-5.1.3), so each answer finds the one ask it is for.
        var asked = DashExchange.Awaits(new DashPhotoAsk(PhotoId));

        Assert.Equal(asked, DashExchange.Answers(new DashPhoto(PhotoId, Jpeg)));
        Assert.NotEqual(asked, DashExchange.Answers(new DashPhoto("a0000000000000001", Jpeg)));
        Assert.True(asked!.Value.IsOn(WatchIdentity.DashPhotoPath));
    }

    [Fact]
    public void ARolodexPageIsAnsweredOnlyForItsPage()
    {
        // B6: the wrist asks the Rolodex a page at a time, so each answer finds the ask for its own page.
        var asked = DashExchange.Awaits(new DashRolodexAsk(0));

        Assert.Equal(asked, DashExchange.Answers(Messages["rolodex"]));
        Assert.NotEqual(asked, DashExchange.Answers(new DashRolodex(1, 2, Edition, [])));
        Assert.NotEqual(DashExchange.Awaits(new DashRolodexAsk(1)), asked);
        Assert.True(asked!.Value.IsOn(WatchIdentity.DashRolodexPath));
        Assert.Equal(DashWaits.Answer, DashExchange.WaitFor(new DashRolodexAsk(0)));
    }

    [Fact]
    public void ANewTextIsAnsweredByItsId()
    {
        var asked = DashExchange.Awaits(Messages["new text to a contact"]);

        Assert.Equal(asked, DashExchange.Answers(new DashSent("send-3", DashSendOutcome.Sent)));
        Assert.Equal(DashWaits.SendAnswer, DashExchange.WaitFor(Messages["new text to a contact"]));
    }

    [Fact]
    public void ACallIsAnsweredForItsMoment()
    {
        // B7: the wrist asks which call ended at the moment it heard it end; Post's answer, a call or none, carries it back.
        var asked = DashExchange.Awaits(new DashCallAsk(T0));

        Assert.Equal(asked, DashExchange.Answers(Messages["call"]));
        Assert.Equal(asked, DashExchange.Answers(Messages["no call"]));
        Assert.NotEqual(asked, DashExchange.Answers(new DashCall(T0.AddSeconds(1), null, null, null)));
        Assert.True(asked!.Value.IsOn(WatchIdentity.DashCallNotePath));
        Assert.Equal(DashWaits.Answer, DashExchange.WaitFor(new DashCallAsk(T0)));
    }

    [Fact]
    public void APhotoWaitsLongerThanAConversation()
    {
        // Post fetches the picture (through Google Messages, or from the phone's own store) and shrinks it before it answers.
        Assert.Equal(TimeSpan.FromSeconds(30), DashWaits.Photo);
        Assert.Equal(DashWaits.Photo, DashExchange.WaitFor(new DashPhotoAsk(PhotoId)));
        Assert.True(DashWaits.Photo > DashWaits.Answer);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("file a correspondence")]
    [InlineData("delete a message")]
    [InlineData("undo")]
    [InlineData("note on a call")]
    [InlineData("arrived")]
    public void ATellWaitsForNothing(string name) => Assert.Null(DashExchange.Awaits(Messages[name]));

    [Theory]
    [InlineData("people ask")]
    [InlineData("thread ask")]
    [InlineData("mail ask")]
    [InlineData("photo ask")]
    [InlineData("rolodex ask")]
    [InlineData("reply")]
    [InlineData("new text to a contact")]
    [InlineData("read")]
    [InlineData("call ask")]
    [InlineData("note on a call")]
    [InlineData("arrived")]
    public void WhatTheWristSendsIsNoAnswer(string name) => Assert.Null(DashExchange.Answers(Messages[name]));

    [Fact]
    public void AnAnswerIsOnTheAsksPath()
    {
        foreach (var (asked, answer) in new (DashMessage, DashMessage)[]
        {
            (new DashPeopleAsk(), People),
            (new DashThreadAsk(GroupKey), GroupThread),
            (new DashSend("send-1", LeeKey, [], "On my way."), new DashSent("send-1", DashSendOutcome.Sent)),
            (new DashMailAsk(), Messages["mail"]),
            (new DashPhotoAsk(PhotoId), Messages["photo"]),
        })
        {
            Assert.Equal(asked.Path, DashExchange.Awaits(asked)!.Value.Path);
            Assert.Equal(answer.Path, DashExchange.Answers(answer)!.Value.Path);
        }
    }

    [Fact]
    public void ARefusalReachesEveryAskOnItsPath()
    {
        var people = DashExchange.Awaits(new DashPeopleAsk())!.Value;
        var thread = DashExchange.Awaits(new DashThreadAsk(LeeKey))!.Value;

        Assert.True(people.IsOn(WatchIdentity.DashPeoplePath));
        Assert.False(thread.IsOn(WatchIdentity.DashPeoplePath));
        Assert.True(thread.IsOn(WatchIdentity.DashThreadPath));
    }

    [Fact]
    public void ASendWaitsOutPostsWholeTurn()
    {
        // Post carries a send out inside a turn of 60 s and waits 15 s for its answer to land (B2's DashReplies).
        Assert.True(DashExchange.WaitFor(Messages["reply"]) >= TimeSpan.FromSeconds(75));
        Assert.True(DashExchange.WaitFor(new DashPeopleAsk()) < DashExchange.WaitFor(Messages["reply"]));
    }

    [Fact]
    public void EveryWaitIsBounded()
    {
        TimeSpan[] waits =
        [
            DashWaits.Find, DashWaits.Deliver, DashWaits.Answer, DashWaits.SendAnswer, DashWaits.Photo,
            DashExchange.WaitFor(new DashPeopleAsk()), DashExchange.WaitFor(Messages["reply"]),
        ];

        Assert.All(waits, wait => Assert.InRange(wait, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(2)));
    }

    [Fact]
    public void TheWaitsAreTheDecidedOnes()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), DashWaits.Find);
        Assert.Equal(TimeSpan.FromSeconds(10), DashWaits.Deliver);
        Assert.Equal(TimeSpan.FromSeconds(20), DashWaits.Answer);
        Assert.Equal(TimeSpan.FromSeconds(80), DashWaits.SendAnswer);
    }
}
