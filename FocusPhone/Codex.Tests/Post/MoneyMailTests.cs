using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Post;

/// <summary>
/// Roadmap #4, narrowed: Post PARSES and Index CONSUMES. A balance is Index's business and
/// crosses to it; a bill is Post's and stays, showing its amount and due date on the row.
/// They are different things and the parser has to keep them apart — handing Index an
/// amount due as though it were a balance would put a debt on the wrong side of a ledger.
///
/// The parsing is general rather than written against two particular lenders.
/// </summary>
public class MoneyMailTests
{
    [Theory]
    [InlineData("Your payment of $42.10 is due on 3 September 2026", 42.10)]
    [InlineData("Amount due: $1,234.56", 1234.56)]
    [InlineData("Minimum payment due $75", 75)]
    public void AnAmountDueIsRead(string body, decimal expected)
    {
        Assert.Equal(expected, MoneyMail.Read(subject: null, body).AmountDue);
    }

    [Theory]
    [InlineData("Your current balance is $2,500.00")]
    [InlineData("Statement balance: $2,500.00")]
    [InlineData("Balance as of today: $2,500.00")]
    public void ABalanceIsRead(string body)
    {
        Assert.Equal(2500.00m, MoneyMail.Read(subject: null, body).Balance);
    }

    // The distinction the whole thing exists for. A statement usually carries both, and
    // they must not be read as one another.
    [Fact]
    public void ABalanceAndAnAmountDueAreKeptApart()
    {
        var facts = MoneyMail.Read(
            subject: "Your statement is ready",
            body: "Your current balance is $2,500.00. Minimum payment due $75.00 by 15 September 2026.");

        Assert.Equal(2500.00m, facts.Balance);
        Assert.Equal(75.00m, facts.AmountDue);
    }

    [Theory]
    [InlineData("Payment due on 3 September 2026", 2026, 9, 3)]
    [InlineData("due by 15 Sep 2026", 2026, 9, 15)]
    [InlineData("Due date: 2026-09-03", 2026, 9, 3)]
    [InlineData("payment due 09/03/2026", 2026, 9, 3)]
    public void ADueDateIsRead(string body, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), MoneyMail.Read(subject: null, body).DueOn);
    }

    // Most mail carries no money at all, and a parser that finds some anyway is worse than
    // one that finds none: a fabricated balance would reach Index and be believed.
    [Theory]
    [InlineData("Are we still on for seven?")]
    [InlineData("Your order has shipped")]
    [InlineData("")]
    [InlineData(null)]
    public void OrdinaryMailCarriesNoMoney(string? body)
    {
        var facts = MoneyMail.Read(subject: null, body);

        Assert.Null(facts.Balance);
        Assert.Null(facts.AmountDue);
        Assert.Null(facts.DueOn);
    }

    // A number that is not money must not be read as money. This is the failure that would
    // put "$2,026" on a row because a year happened to be nearby.
    [Theory]
    [InlineData("Your appointment is at 2:30 on the 3rd")]
    [InlineData("Order 12345 confirmed")]
    public void BareNumbersAreNotMoney(string body)
    {
        var facts = MoneyMail.Read(subject: null, body);

        Assert.Null(facts.Balance);
        Assert.Null(facts.AmountDue);
    }

    // ---- what crosses to Index ----

    // The payload is balance and date only. Deliberately minimal: Index needs to know what
    // is owed and when it was true, and nothing about who the owner banks with belongs in
    // an intent that any signed app could receive.
    [Fact]
    public void OnlyABalanceCrossesToIndex()
    {
        var facts = new MoneyFacts(AmountDue: 75m, DueOn: new DateOnly(2026, 9, 15), Balance: 2500m,
            AsOf: new DateOnly(2026, 9, 1));

        var payload = IndexHandoff.PayloadFor(facts);

        Assert.NotNull(payload);
        Assert.Equal(2500m, payload!.Balance);
        Assert.Equal(new DateOnly(2026, 9, 1), payload.AsOf);
    }

    // Nothing to send is not an empty send. Index sees the email again on a later sync,
    // and a handoff with no balance in it is noise on a channel that has no queue.
    [Fact]
    public void MailWithNoBalanceCrossesNothing()
    {
        var facts = new MoneyFacts(AmountDue: 75m, DueOn: new DateOnly(2026, 9, 15), Balance: null, AsOf: null);

        Assert.Null(IndexHandoff.PayloadFor(facts));
    }

    // A balance with no date is still worth sending — Index can stamp it with arrival —
    // but it must say so rather than inventing one.
    [Fact]
    public void ABalanceWithNoDateStillCrosses()
    {
        var payload = IndexHandoff.PayloadFor(new MoneyFacts(null, null, Balance: 2500m, AsOf: null));

        Assert.NotNull(payload);
        Assert.Null(payload!.AsOf);
    }
}
