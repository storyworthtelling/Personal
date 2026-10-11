using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Tests.People;

/// <summary>
/// Whose phone this is — the one answer every screen asks (D97: each viewer's lists are private). The
/// four screens each used to answer it themselves, and the copies drifted: one took the roster's
/// first adult, which on a two-adult household shows one adult the other's private list. The rule now
/// lives once: the install's recorded viewer, if that viewer is still an adult on the roster; else the
/// roster's only adult; else nobody.
/// </summary>
public class ThisPhoneTests
{
    private static readonly ViewerId Ash = new("ash-example");
    private static readonly ViewerId Rowan = new("rowan-example");
    private static readonly ViewerId Wren = new("wren-example");

    private static HouseholdRoster TwoAdultsAndAChild() => new([new(Ash, false), new(Rowan, false), new(Wren, true)]);

    [Fact]
    public void NoRosterIsNobodyEvenWithARecord()
    {
        Assert.Equal(new ThisPhoneAnswer(ThisPhoneStatus.NoRoster, null), ThisPhone.Resolve(null, null));
        Assert.Equal(new ThisPhoneAnswer(ThisPhoneStatus.NoRoster, null), ThisPhone.Resolve(null, Ash));
    }

    [Fact]
    public void TheRecordedAdultIsTheAnswerInATwoAdultHousehold()
    {
        var answer = ThisPhone.Resolve(TwoAdultsAndAChild(), Rowan);

        Assert.Equal(ThisPhoneStatus.Known, answer.Status);
        Assert.Equal(Rowan, answer.Viewer);
    }

    [Fact]
    public void WithNoRecordTheOnlyAdultIsTheAnswer()
    {
        var answer = ThisPhone.Resolve(new HouseholdRoster([new(Ash, false), new(Wren, true)]), null);

        Assert.Equal(ThisPhoneStatus.Known, answer.Status);
        Assert.Equal(Ash, answer.Viewer);
    }

    /// <summary>D97: a guess between two adults shows one of them the other's private list.</summary>
    [Fact]
    public void WithNoRecordTwoAdultsIsNobodyNeverTheFirst()
    {
        var answer = ThisPhone.Resolve(TwoAdultsAndAChild(), null);

        Assert.Equal(ThisPhoneStatus.Unmatched, answer.Status);
        Assert.Null(answer.Viewer);
    }

    [Fact]
    public void AChildOnlyRosterIsNobody()
    {
        var answer = ThisPhone.Resolve(new HouseholdRoster([new(Wren, true)]), null);

        Assert.Equal(ThisPhoneStatus.Unmatched, answer.Status);
        Assert.Null(answer.Viewer);
    }

    /// <summary>A record that no longer holds is not quietly swapped for whoever is left.</summary>
    [Fact]
    public void ARecordedViewerGoneFromTheRosterIsNobodyEvenWithOneAdultLeft()
    {
        var answer = ThisPhone.Resolve(new HouseholdRoster([new(Ash, false), new(Wren, true)]), Rowan);

        Assert.Equal(ThisPhoneStatus.Unmatched, answer.Status);
        Assert.Null(answer.Viewer);
    }

    [Fact]
    public void ARecordedViewerWhoIsNowAChildIsNobody()
    {
        var answer = ThisPhone.Resolve(new HouseholdRoster([new(Ash, false), new(Rowan, true)]), Rowan);

        Assert.Equal(ThisPhoneStatus.Unmatched, answer.Status);
        Assert.Null(answer.Viewer);
    }

    [Fact]
    public void OnlyAnAdultOnTheRosterCanBeRecorded()
    {
        var roster = TwoAdultsAndAChild();

        Assert.True(ThisPhone.CanBeRecorded(roster, Ash));
        Assert.False(ThisPhone.CanBeRecorded(roster, Wren));
        Assert.False(ThisPhone.CanBeRecorded(roster, new ViewerId("stranger-example")));
        Assert.False(ThisPhone.CanBeRecorded(null, Ash));
    }

    [Fact]
    public void ThereIsOneSentenceForAnUnmatchedPhone() =>
        Assert.Equal("This phone isn't matched to anyone in the household yet.", ThisPhone.UnmatchedSentence);
}
