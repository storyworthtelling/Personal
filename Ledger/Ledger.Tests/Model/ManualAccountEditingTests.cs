using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

public class ManualAccountEditingTests
{
    // ---- the required tint (mockup 15): on APR and Payment until BOTH are there ----

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]   // one filled box does not release the other
    [InlineData(false, true, true)]
    [InlineData(true, true, false)]
    public void TheRequiredTintStaysUntilBothFiguresArePresent(bool apr, bool payment, bool tinted)
    {
        Assert.Equal(tinted, ManualAccountEditing.RequiredTintApplies(apr, payment));
    }

    // ---- validation on blur, then live ----

    [Fact]
    public void ABoxSaysNothingWhileItIsFirstBeingTypedInto()
    {
        var live = new BlurThenLive();

        Assert.Null(live.Typed("Type a number like 6.25."));
    }

    [Fact]
    public void LeavingABadBoxMarksItAndThenEveryKeystrokeRechecksItUntilItClears()
    {
        var live = new BlurThenLive();

        Assert.Equal("bad", live.Blur("bad"));
        Assert.Equal("bad", live.Typed("bad"));
        Assert.Null(live.Typed(null));     // readable now: the rose clears live
        Assert.Null(live.Typed("bad"));    // and stays quiet until the next blur
        Assert.Equal("bad", live.Blur("bad"));
    }

    [Fact]
    public void LeavingAGoodBoxMarksNothing()
    {
        var live = new BlurThenLive();

        Assert.Null(live.Blur(null));
        Assert.Null(live.Typed("bad"));
    }

    [Theory]
    [InlineData("abc", FigureKind.Percent, false, "Type a number like 6.25.")]
    [InlineData("abc", FigureKind.Money, false, "Type an amount like 250 or 1,200.50.")]
    [InlineData("", FigureKind.Percent, false, null)]    // optional and empty is fine
    [InlineData("", FigureKind.Money, true, "Type an amount like 250 or 1,200.50.")]  // required and empty is not
    [InlineData("6.25%", FigureKind.Percent, false, null)]
    public void AFigureBoxErrorIsTheHintOrNothing(string text, FigureKind kind, bool required, string? expected)
    {
        Assert.Equal(expected, ManualAccountEditing.FigureError(text, kind, required));
    }

    [Theory]
    [InlineData(true, true, "Name and balance are required.")]
    [InlineData(true, false, "Name is required.")]
    [InlineData(false, true, "Balance is required.")]
    [InlineData(false, false, null)]
    public void TheAddFormsTopSentenceIsOnlyTheRequiredSummary(bool name, bool balance, string? expected)
    {
        Assert.Equal(expected, ManualAccountEditing.RequiredSummary(name, balance));
    }

    // ---- the Plaid savings post-it: no APR reported, no APR column ----

    [Fact]
    public void ASavingsPostItWithNoAprShowsOnlyAvailable()
    {
        var facts = PostItFacts.For(aprText: null, availableText: "$8,200.00");

        var only = Assert.Single(facts);
        Assert.Equal("Available", only.Label);
        Assert.Equal("$8,200.00", only.Value);
    }

    [Fact]
    public void APostItWithAnAprShowsItBeforeAvailable()
    {
        var facts = PostItFacts.For("22.49%", "$5,000.00");

        Assert.Equal(["APR", "Available"], facts.Select(f => f.Label));
    }

    [Fact]
    public void AnEmptyPatchIsEmptyAndANamedFieldIsNot()
    {
        Assert.True(new ManualAccountPatch().IsEmpty);
        Assert.False(new ManualAccountPatch { Apr = 0m }.IsEmpty);
    }
    // ---- remediation 3.8: the post-it's patch carries a card's limit and nothing else ----

    [Fact]
    public void ALimitAloneIsAPatch()
    {
        var patch = new ManualAccountPatch { CreditLimit = 5000m };

        Assert.False(patch.IsEmpty);
        Assert.Null(patch.Balance);
        Assert.Null(patch.Apr);
        Assert.Null(patch.Payment);
        Assert.Null(patch.Cadence);
        Assert.Null(patch.NextDue);
    }
}
