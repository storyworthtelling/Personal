using Pulp.Domain.Core;
using Pulp.Domain.FirstRun;

namespace Pulp.Tests.FirstRun;

/// <summary>
/// A person added at first run is written as a viewer record whose id is derived from the name they
/// were given: stable (the same name always gives the same id), readable back as the name, and never
/// the id of someone already on the roster. Invented names.
/// </summary>
public class ViewerNamingTests
{
    [Theory]
    [InlineData("Rowan", "rowan")]
    [InlineData("  Juniper   Vale ", "juniper-vale")]
    [InlineData("Ada-Quill!", "ada-quill")]
    [InlineData("Élodie", "élodie")]
    public void TheIdIsTheNameLowercasedWithWordsJoined(string name, string expected) =>
        Assert.Equal(expected, ViewerNaming.IdFor(name, []).Value);

    [Fact]
    public void TheSameNameGivesTheSameId() =>
        Assert.Equal(ViewerNaming.IdFor("Juniper Vale", []), ViewerNaming.IdFor("Juniper Vale", []));

    [Fact]
    public void AnIdAlreadyOnTheRosterIsNeverHandedOutAgain()
    {
        var taken = new[] { new ViewerId("rowan"), new ViewerId("rowan-2") };

        Assert.Equal("rowan-3", ViewerNaming.IdFor("ROWAN", taken).Value);
    }

    [Fact]
    public void ANameWithNoLetterOrDigitHasNoId() =>
        Assert.Throws<ArgumentException>(() => ViewerNaming.IdFor("?!", []));

    [Theory]
    [InlineData("rowan", "Rowan")]
    [InlineData("juniper-vale", "Juniper Vale")]
    public void AnIdReadsBackAsAName(string id, string expected) =>
        Assert.Equal(expected, ViewerNaming.Display(new ViewerId(id)));

    [Fact]
    public void AnEmptyNameIsAskedFor() =>
        Assert.Equal(ViewerNaming.NameNeeded, ViewerNaming.Check("   ", []));

    [Fact]
    public void ANameOfOnlyPunctuationIsRefusedInPlainWords() =>
        Assert.Equal(ViewerNaming.NeedsALetter, ViewerNaming.Check("--", []));

    [Fact]
    public void ANameSomeoneAlreadyHasIsRefusedRatherThanNumbered() =>
        Assert.Equal(ViewerNaming.AlreadyThere("Rowan"), ViewerNaming.Check("rowan", [new ViewerId("rowan")]));

    [Fact]
    public void AGoodNameHasNoProblem() =>
        Assert.Null(ViewerNaming.Check("Juniper", [new ViewerId("rowan")]));

    [Fact]
    public void TheSentencesArePlainAndEndInAFullStop()
    {
        var sentences = new[] { ViewerNaming.NameNeeded, ViewerNaming.NeedsALetter, ViewerNaming.AlreadyThere("Rowan") };

        Assert.NotEmpty(sentences);
        Assert.All(sentences, sentence => Assert.EndsWith(".", sentence));
    }
}
