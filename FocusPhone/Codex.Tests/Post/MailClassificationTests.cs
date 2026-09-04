using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Machine mail gets a row like everything else — everything that arrived appears — but it
/// gets a quiet band at the bottom and it ages out by itself, so the Mailbox stays a list
/// of things to deal with rather than a list of things that happened.
///
/// Rules first, Claude only for what the rules cannot place. That order matters for cost
/// and for privacy, and it means an absent or rejected key degrades to rules-only rather
/// than to nothing.
/// </summary>
public class MailClassificationTests
{
    // The rules answer TWO questions, and conflating them is how a classifier goes wrong.
    // List-Unsubscribe is a header a person's mail client does not send, so it settles
    // "is this machine mail" outright — while saying nothing about WHICH kind of list it
    // is, which is exactly what the model is for.
    [Fact]
    public void ListUnsubscribeSettlesMachineMailButNotItsKind()
    {
        var verdict = MailRules.Classify(new MailSignals(
            "news@shop.example", "Spring sale", HasListUnsubscribe: true));

        Assert.True(verdict.KnownMachine);
        Assert.Null(verdict.Kind);
    }

    // no-reply is the sender telling you outright that nobody is listening.
    [Theory]
    [InlineData("no-reply@bank.example")]
    [InlineData("noreply@bank.example")]
    [InlineData("do-not-reply@bank.example")]
    [InlineData("donotreply@bank.example")]
    public void ANoReplySenderIsNeverHuman(string from)
    {
        var verdict = MailRules.Classify(new MailSignals(from, "Hello there", HasListUnsubscribe: false));

        Assert.True(verdict.KnownMachine);
        Assert.NotEqual(MailClass.Human, verdict.Kind);
    }

    // A bill is the one kind that never ages out, so it is worth recognising by rule
    // rather than leaving to a model that might have an off day.
    [Theory]
    [InlineData("Your statement is ready")]
    [InlineData("Payment due 3 September")]
    [InlineData("Your invoice from the plumber")]
    [InlineData("Amount due: $42.10")]
    public void StatementAndInvoiceLanguageReadsAsABill(string subject)
    {
        Assert.Equal(MailClass.Bill,
            MailRules.Classify(new MailSignals("no-reply@bank.example", subject, HasListUnsubscribe: false)).Kind);
    }

    // An ordinary message from a person has none of those marks, and the rules must say so
    // rather than guessing — a person's mail wrongly filed as marketing ages out in a day.
    [Fact]
    public void AnOrdinaryMessageIsLeftToBeJudged()
    {
        var verdict = MailRules.Classify(new MailSignals(
            "her@example.com", "are we still on?", HasListUnsubscribe: false));

        Assert.False(verdict.KnownMachine);
        Assert.Null(verdict.Kind);
    }

    // ---- ageing ----

    // The lifetimes the owner chose. Bills are the exception and never age out at all.
    [Theory]
    [InlineData(MailClass.Marketing, 20, false)]
    [InlineData(MailClass.Marketing, 30, true)]
    [InlineData(MailClass.Newsletter, 6 * 24, false)]
    [InlineData(MailClass.Newsletter, 8 * 24, true)]
    [InlineData(MailClass.Transactional, 40, false)]
    [InlineData(MailClass.Transactional, 60, true)]
    public void EachKindHasItsOwnLifetime(MailClass kind, int hoursOld, bool expectGone)
    {
        var arrived = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(expectGone, MailAgeing.HasExpired(kind, arrived, arrived.AddHours(hoursOld)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(365)]
    [InlineData(3650)]
    public void ABillNeverAgesOut(int daysOld)
    {
        var arrived = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.False(MailAgeing.HasExpired(MailClass.Bill, arrived, arrived.AddDays(daysOld)));
    }

    // A person's message never ages out either. Only trivia does — that is the whole
    // distinction the classification exists to draw.
    [Theory]
    [InlineData(1)]
    [InlineData(3650)]
    public void AHumanMessageNeverAgesOut(int daysOld)
    {
        var arrived = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero);

        Assert.False(MailAgeing.HasExpired(MailClass.Human, arrived, arrived.AddDays(daysOld)));
    }

    // ---- the owner's corrections ----

    // Correcting a sender is permanent: it teaches Post for good rather than for one
    // message. Being wrong about the same sender twice is the thing that would wear the
    // feature out.
    [Fact]
    public void PromotingASenderIsPermanentAndOverridesTheRules()
    {
        var promotions = new SenderPromotions();
        promotions.Promote("news@shop.example");

        var signals = new MailSignals("news@shop.example", "Spring sale", HasListUnsubscribe: true);

        Assert.Equal(MailClass.Human, promotions.Apply(signals, MailRules.Classify(signals).Kind));
    }

    [Fact]
    public void AnUnpromotedSenderKeepsWhateverTheRulesSaid()
    {
        var promotions = new SenderPromotions();
        var signals = new MailSignals("news@shop.example", "Spring sale", HasListUnsubscribe: true);

        Assert.Equal(MailRules.Classify(signals).Kind, promotions.Apply(signals, MailRules.Classify(signals).Kind));
    }

    // The address is matched case- and whitespace-insensitively; senders do not write
    // their own headers consistently.
    [Theory]
    [InlineData("News@Shop.Example")]
    [InlineData("  news@shop.example  ")]
    public void PromotionMatchesTheSenderHoweverItIsWritten(string incoming)
    {
        var promotions = new SenderPromotions();
        promotions.Promote("news@shop.example");

        var signals = new MailSignals(incoming, "Spring sale", HasListUnsubscribe: true);
        Assert.Equal(MailClass.Human, promotions.Apply(signals, MailClass.Marketing));
    }

    // Demotion exists too: a sender the owner is tired of goes back to being trivia.
    [Fact]
    public void ASenderCanBeDemotedAgain()
    {
        var promotions = new SenderPromotions();
        promotions.Promote("news@shop.example");
        promotions.Demote("news@shop.example");

        var signals = new MailSignals("news@shop.example", "Spring sale", HasListUnsubscribe: true);
        Assert.Equal(MailClass.Marketing, promotions.Apply(signals, MailClass.Marketing));
    }
}
