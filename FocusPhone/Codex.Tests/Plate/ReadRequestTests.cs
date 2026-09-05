using Codex.Domain.Plate;
using Codex.Domain.Post;
using Xunit;

namespace Codex.Tests.Plate;

/// <summary>
/// The inbound half of the handoff: another app asks, Plate looks, Plate returns.
///
/// <para>The walkthrough moved <b>reading</b> from the outbound side to this one, which is
/// the simpler contract — the app that wants the data already knows what it wants, so Plate
/// never has to work out what a piece of paper is for.</para>
/// </summary>
public class ReadRequestTests
{
    [Fact]
    public void EachReadHasItsOwnAction()
    {
        // Two actions rather than one with a "kind" extra: a caller that sends the wrong
        // kind gets an activity-not-found it can see, instead of a result it cannot use.
        Assert.NotEqual(ReadRequest.ReadCard, ReadRequest.ReadStatement);
        Assert.StartsWith("com.storyworthtelling.plate.action.", ReadRequest.ReadCard,
            StringComparison.Ordinal);
        Assert.StartsWith("com.storyworthtelling.plate.action.", ReadRequest.ReadStatement,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ACardComesBackAsThreePlainStrings()
    {
        var extras = ReadRequest.CardExtras(
            new CardReading("Ada Speight", "(503) 555-0142", "ada@harbourline.com"));

        Assert.Equal("Ada Speight", extras[ReadRequest.NameExtra]);
        Assert.Equal("(503) 555-0142", extras[ReadRequest.PhoneExtra]);
        Assert.Equal("ada@harbourline.com", extras[ReadRequest.EmailExtra]);
    }

    [Fact]
    public void AnEmptyFieldIsLeftOutRatherThanSentBlank()
    {
        // A blank extra and a missing one mean different things to the caller: one is "the
        // card says nothing here", the other is "I filled this in with nothing".
        var extras = ReadRequest.CardExtras(new CardReading("Ada Speight", "", ""));

        Assert.True(extras.ContainsKey(ReadRequest.NameExtra));
        Assert.False(extras.ContainsKey(ReadRequest.PhoneExtra));
        Assert.False(extras.ContainsKey(ReadRequest.EmailExtra));
    }

    [Fact]
    public void AStatementCrossesOnTheExistingIndexContractUnchanged()
    {
        // G9. Plate becomes a second sender of the balance Post already sends, and the
        // payload has to be byte-identical or Index would need to know which app spoke.
        var facts = MoneyMail.Read(null, "Your current balance is $2,500.00 as of 2026-08-31.");
        var extras = ReadRequest.StatementExtras(facts);

        Assert.NotNull(extras);
        Assert.Equal("2500.00", extras![IndexHandoff.BalanceExtra]);
        Assert.Equal("2026-08-31", extras[IndexHandoff.AsOfExtra]);
    }

    [Fact]
    public void AStatementWithNoBalanceIsNothingToSend()
    {
        // Errs toward finding nothing: a fabricated balance would reach Index and be
        // believed, which is far worse than one the owner has to read themselves.
        var facts = MoneyMail.Read(null, "Thank you for banking with us.");

        Assert.Null(ReadRequest.StatementExtras(facts));
    }

    [Fact]
    public void ADatelessBalanceCarriesNoDateKeyAtAll()
    {
        var facts = MoneyMail.Read(null, "Your balance is $75.00");
        var extras = ReadRequest.StatementExtras(facts);

        Assert.NotNull(extras);
        Assert.Equal("75.00", extras![IndexHandoff.BalanceExtra]);
        Assert.False(extras.ContainsKey(IndexHandoff.AsOfExtra));
    }
}
