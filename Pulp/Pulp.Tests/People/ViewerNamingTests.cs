using Pulp.Domain.Core;
using Pulp.Domain.People;

namespace Pulp.Tests.People;

/// <summary>
/// A person's name and the id they are written under, in the one place both first run and the
/// Household page ask. The id keys everything the person will own (sittings, vetoes, playlists, a
/// child's rules), so it is minted once, stable for the same name, readable back as the name, and
/// never an id already on the roster — a second person of the same name must be a second id or they
/// would silently take over the first's records. Invented names.
/// </summary>
public class ViewerNamingTests
{
    [Theory]
    [InlineData("  Robin  ", "Robin")]
    [InlineData("Mary   Ann", "Mary Ann")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void ATypedNameIsTidied(string? typed, string? expected) => Assert.Equal(expected, ViewerNaming.Clean(typed));

    [Theory]
    [InlineData("Rowan", "rowan")]
    [InlineData("  Juniper   Vale ", "juniper-vale")]
    [InlineData("Ada-Quill!", "ada-quill")]
    [InlineData("Élodie", "élodie")]
    public void TheIdIsTheNameLowercasedWithWordsJoined(string name, string expected) =>
        Assert.Equal(expected, ViewerNaming.Mint(name, []).Value);

    [Fact]
    public void AnIdAlreadyOnTheRosterIsNeverHandedOutAgain()
    {
        var taken = new[] { new ViewerId("rowan"), new ViewerId("rowan-2") };

        Assert.Equal("rowan-3", ViewerNaming.Mint("ROWAN", taken).Value);
    }

    [Fact]
    public void TakenIsComparedWithoutCase() =>
        Assert.Equal("robin-2", ViewerNaming.Mint("robin", [new ViewerId("Robin")]).Value);

    [Fact]
    public void ANameWithNoLetterOrDigitHasNoId()
    {
        Assert.Throws<ArgumentException>(() => ViewerNaming.Mint("?!", []));
        Assert.Throws<ArgumentException>(() => ViewerNaming.Mint("  ", []));
    }

    [Theory]
    [InlineData("rowan", "Rowan")]
    [InlineData("juniper-vale", "Juniper Vale")]
    public void AnIdReadsBackAsAName(string id, string expected) =>
        Assert.Equal(expected, ViewerNaming.Display(new ViewerId(id)));

    [Fact]
    public void ABlankNameIsRefusedWithOneSentence() =>
        Assert.Equal(ViewerNaming.NameNeeded, ViewerNaming.Check("   ", []));

    [Fact]
    public void ANameWithNoLetterIsRefusedWithOneSentence() =>
        Assert.Equal(ViewerNaming.NeedsALetter, ViewerNaming.Check("--", []));

    [Fact]
    public void ANameAlreadyOnTheRosterIsRefused() =>
        Assert.Equal(ViewerNaming.AlreadyThere("Rowan"), ViewerNaming.Check("rowan", [new ViewerId("rowan")]));

    [Fact]
    public void AFreshNameIsAccepted() =>
        Assert.Null(ViewerNaming.Check("Juniper", [new ViewerId("rowan")]));

    [Fact]
    public void EverySentenceIsOneSentenceWithNoRawId()
    {
        var sentences = new[] { ViewerNaming.NameNeeded, ViewerNaming.NeedsALetter, ViewerNaming.AlreadyThere("Rowan") };

        Assert.All(sentences, sentence => Assert.EndsWith(".", sentence, StringComparison.Ordinal));
    }

    /// <summary>There is one ViewerNaming: a second copy would let the two screens disagree about an id.</summary>
    [Fact]
    public void ThereIsExactlyOneViewerNamingInTheDomain()
    {
        var copies = PulpSource.Files(".cs")
            .Where(file => Path.GetFileName(file) == "ViewerNaming.cs")
            .Select(PulpSource.Relative)
            .ToList();

        Assert.Equal(["Pulp.Domain/People/ViewerNaming.cs"], copies);
    }
}
