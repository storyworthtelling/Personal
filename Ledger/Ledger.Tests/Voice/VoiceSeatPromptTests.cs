using System.Text.RegularExpressions;
using Index.Domain.Voice;

namespace Index.Tests.Voice;

/// <summary>
/// R-G29 (a): each seat's pinned instruction forbids what D114 forbids, and the closed list of occasions
/// stays closed. The instruction is the only free text the model is given besides the facts, so it is
/// held to a standard here the way Foreword's <c>HandwritingInstruction</c> is pinned.
/// </summary>
public class VoiceSeatPromptTests
{
    private static readonly string[] Trouble =
        ["error", "fail", "failed", "failure", "problem", "sorry", "unable", "denied", "invalid", "expired", "crash"];

    [Theory]
    [InlineData(VoiceSeat.Accounts)]
    [InlineData(VoiceSeat.Snowball)]
    public void AccountsAndSnowballInstructionsForbidDollarAmountsAndAccountIdentity(VoiceSeat seat)
    {
        var instruction = VoiceSeatInstructions.For(seat);

        Assert.Contains("dollar amount", instruction, StringComparison.Ordinal);
        Assert.Contains("account number", instruction, StringComparison.Ordinal);
        Assert.Contains("bank name", instruction, StringComparison.Ordinal);
        Assert.Contains("account name", instruction, StringComparison.Ordinal);
        AssertCommonRules(instruction);
    }

    [Fact]
    public void SavingsInstructionForbidsBalancesAndGoalsAndAllowsOnlyTheStatedRate()
    {
        var instruction = VoiceSeatInstructions.For(VoiceSeat.Savings);

        Assert.Contains("balance", instruction, StringComparison.Ordinal);
        Assert.Contains("goal", instruction, StringComparison.Ordinal);
        Assert.Contains("total", instruction, StringComparison.Ordinal);
        Assert.Contains("account name", instruction, StringComparison.Ordinal);
        Assert.Contains("bank name", instruction, StringComparison.Ordinal);
        Assert.Contains("only the monthly rate", instruction, StringComparison.Ordinal);
        AssertCommonRules(instruction);
    }

    private static void AssertCommonRules(string instruction)
    {
        Assert.Contains("ONLY from the FACTS", instruction, StringComparison.Ordinal);
        Assert.Contains("one short sentence", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no advice", instruction, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("invent", instruction, StringComparison.OrdinalIgnoreCase);
        // A pinned instruction is words: a digit in it is a number the model was handed without the facts.
        Assert.DoesNotMatch(new Regex(@"[\d$]"), instruction);
    }

    [Fact]
    public void ThePromptIsTheInstructionThenTheFactsAndNothingElse()
    {
        var facts = VoiceFactBuilder.Snowball(VoiceTestSupport.TwelvePercentDown());

        var prompt = VoiceSeatPrompts.Build(facts)!;

        Assert.Equal(VoicedOccasions.For(VoiceSeat.Snowball), prompt.Occasion);
        Assert.StartsWith(VoiceSeatInstructions.For(VoiceSeat.Snowball), prompt.Context, StringComparison.Ordinal);
        foreach (var line in facts.Lines)
        {
            Assert.Contains($"- {line}", prompt.Context, StringComparison.Ordinal);
        }
        Assert.True(prompt.MaxWords is > 0 and <= 30, "A seat is one short sentence, not a paragraph.");
    }

    [Fact]
    public void NoFactsIsNoPrompt()
    {
        Assert.Null(VoiceSeatPrompts.Build(VoiceFacts.None(VoiceSeat.Accounts)));
    }

    [Fact]
    public void EverySeatHasItsOwnOccasionAndNoneReadsAsTrouble()
    {
        Assert.Equal(Enum.GetValues<VoiceSeat>().Length, VoicedOccasions.All.Distinct().Count());
        foreach (var occasion in VoicedOccasions.All)
        {
            var offending = Trouble.FirstOrDefault(word => occasion.Contains(word, StringComparison.OrdinalIgnoreCase));
            Assert.True(offending is null,
                $"The occasion \"{occasion}\" reads as trouble (\"{offending}\"). Errors are told in plain copy.");
        }
    }

    // ---- the reply, checked before it is shown ----

    [Theory]
    [InlineData(VoiceSeat.Accounts)]
    [InlineData(VoiceSeat.Snowball)]
    public void ARepliesWithACurrencySignOrAFigureIsSetAsideOnTheSeatsThatSpeakNoAmounts(VoiceSeat seat)
    {
        Assert.Null(VoiceReplyCheck.Accept(seat, "You owe $12,000 less than in August."));
        Assert.Null(VoiceReplyCheck.Accept(seat, "Down 12000 dollars since August."));
        Assert.Equal("Down about one percent since August.",
            VoiceReplyCheck.Accept(seat, "  Down about one percent since August.\n"));
    }

    [Fact]
    public void SavingsMayNameItsRateButNotMoreThanOneSentence()
    {
        Assert.Equal("Three hundred a month, month after month.",
            VoiceReplyCheck.Accept(VoiceSeat.Savings, "Three hundred a month, month after month."));
        Assert.Equal("About $300 a month, month after month.",
            VoiceReplyCheck.Accept(VoiceSeat.Savings, "About $300 a month, month after month."));
        Assert.Null(VoiceReplyCheck.Accept(VoiceSeat.Savings, "Three hundred a month.\nKeep it up, and add more."));
    }

    [Theory]
    [InlineData(VoiceSeat.Accounts)]
    [InlineData(VoiceSeat.Snowball)]
    [InlineData(VoiceSeat.Savings)]
    public void BlankOrRunawayRepliesAreSetAside(VoiceSeat seat)
    {
        Assert.Null(VoiceReplyCheck.Accept(seat, null));
        Assert.Null(VoiceReplyCheck.Accept(seat, "   "));
        Assert.Null(VoiceReplyCheck.Accept(seat, string.Join(' ', Enumerable.Repeat("word", 80))));
    }
}
