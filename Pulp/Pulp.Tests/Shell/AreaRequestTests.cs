using Pulp.Domain.Shell;

namespace Pulp.Tests.Shell;

/// <summary>
/// A sub-page's binder tab opens the shell on the tapped area. The title page and Recently watched
/// draw the gutter but used to just close, landing on whatever area was showing underneath; the
/// shell now reads which area to open from its intent, and these words are that request.
/// </summary>
public class AreaRequestTests
{
    [Fact]
    public void EveryAreaRoundTripsThroughItsWord()
    {
        Assert.NotEmpty(PulpAreas.All);
        foreach (var spec in PulpAreas.All)
        {
            Assert.Equal(spec.Area, AreaRequest.Read(AreaRequest.Word(spec.Area)));
        }
    }

    [Fact]
    public void EveryAreaHasItsOwnWord() =>
        Assert.Equal(PulpAreas.All.Count, PulpAreas.All.Select(spec => AreaRequest.Word(spec.Area)).Distinct(StringComparer.OrdinalIgnoreCase).Count());

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("somewhere-else")]
    public void AnAbsentOrUnknownWordAsksForNothing(string? word) => Assert.Null(AreaRequest.Read(word));

    /// <summary>The sub-pages draw their tabs in <see cref="PulpAreas.All"/>'s order, so a tab's index names its area.</summary>
    [Fact]
    public void ATabIndexIsTheStripsArea()
    {
        for (var index = 0; index < PulpAreas.All.Count; index++)
        {
            Assert.Equal(PulpAreas.All[index].Area, AreaRequest.ForTab(index));
            Assert.Equal(index, AreaRequest.IndexOf(PulpAreas.All[index].Area));
        }

        Assert.Null(AreaRequest.ForTab(-1));
        Assert.Null(AreaRequest.ForTab(PulpAreas.All.Count));
    }

    [Fact]
    public void TheExtraIsNamed() => Assert.False(string.IsNullOrWhiteSpace(AreaRequest.Extra));
}
