using Pulp.Domain.Core;
using Pulp.Domain.Settings;

namespace Pulp.Tests.Settings;

/// <summary>A new viewer's name and id (D88). The id keys everything they will own, so it never repeats.</summary>
public class ViewerNamingTests
{
    [Theory]
    [InlineData("  Robin  ", "Robin")]
    [InlineData("Mary   Ann", "Mary Ann")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void ATypedNameIsTidied(string? typed, string? expected) => Assert.Equal(expected, ViewerNaming.Clean(typed));

    [Fact]
    public void AnIdIsTheNameInLowerCaseWords() =>
        Assert.Equal(new ViewerId("mary-ann"), ViewerNaming.Mint("Mary Ann", []));

    /// <summary>Two people of one name must be two ids, or the second would silently take over the first's records.</summary>
    [Fact]
    public void ATakenIdIsNeverHandedOutAgain()
    {
        var taken = new[] { new ViewerId("robin"), new ViewerId("robin-2") };

        Assert.Equal(new ViewerId("robin-3"), ViewerNaming.Mint("Robin", taken));
    }

    [Fact]
    public void TakenIsComparedWithoutCase() =>
        Assert.Equal(new ViewerId("robin-2"), ViewerNaming.Mint("robin", [new ViewerId("Robin")]));

    [Fact]
    public void ANameWithNoLettersStillGetsAnId() =>
        Assert.Equal(new ViewerId("viewer"), ViewerNaming.Mint("☺☺", []));

    [Fact]
    public void ABlankNameIsRefused() => Assert.Throws<ArgumentException>(() => ViewerNaming.Mint("  ", []));
}
