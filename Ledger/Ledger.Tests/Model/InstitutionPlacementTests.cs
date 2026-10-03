using Index.Domain.Model;
using Xunit;

namespace Index.Tests.Model;

/// <summary>
/// Remediation 3.3 item 6 (mockup 2, frame 2): the bank action card's subtitle is "4 accounts · first on
/// the page", and its Move up row is dimmed on the first bank (Move down on the last). The page used to
/// pass <c>canMoveUp: true, canMoveDown: true</c> for every bank, so the dim state was unreachable and
/// tapping the first bank's Move up did nothing and said nothing.
///
/// <para>Mockup 2 draws only the first position. The other phrasings are this file's minimal extension:
/// "last on the page", "2nd of 5 on the page" in the middle, and "the only bank on the page" for a lone
/// bank — read on the same words, in the same order.</para>
/// </summary>
public class InstitutionPlacementTests
{
    private static readonly Guid[] Five = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

    [Fact]
    public void TheFirstBankIsFirstOnThePage_AndCannotMoveUp()
    {
        var placement = InstitutionPlacement.Of(Five, Five[0]);

        Assert.Equal("first on the page", placement.Phrase);
        Assert.False(placement.CanMoveUp);
        Assert.True(placement.CanMoveDown);
        Assert.True(placement.CanMoveToBottom);
    }

    [Fact]
    public void TheLastBankIsLastOnThePage_AndCannotMoveDownOrToTheBottom()
    {
        var placement = InstitutionPlacement.Of(Five, Five[4]);

        Assert.Equal("last on the page", placement.Phrase);
        Assert.True(placement.CanMoveUp);
        Assert.False(placement.CanMoveDown);
        Assert.False(placement.CanMoveToBottom); // already there: the row would do nothing
    }

    [Theory]
    [InlineData(1, "2nd of 5 on the page")]
    [InlineData(2, "3rd of 5 on the page")]
    [InlineData(3, "4th of 5 on the page")]
    public void AMiddleBankSaysItsPlaceOfTheCount(int index, string expected)
    {
        var placement = InstitutionPlacement.Of(Five, Five[index]);

        Assert.Equal(expected, placement.Phrase);
        Assert.True(placement.CanMoveUp);
        Assert.True(placement.CanMoveDown);
    }

    [Fact]
    public void ALoneBankCannotMoveAnywhere()
    {
        var only = new[] { Guid.NewGuid() };

        var placement = InstitutionPlacement.Of(only, only[0]);

        Assert.Equal("the only bank on the page", placement.Phrase);
        Assert.False(placement.CanMoveUp);
        Assert.False(placement.CanMoveDown);
        Assert.False(placement.CanMoveToBottom);
    }

    [Theory]
    [InlineData(10, "11th")]
    [InlineData(11, "12th")]
    [InlineData(12, "13th")]
    [InlineData(20, "21st")]
    [InlineData(21, "22nd")]
    public void OrdinalsAreEnglish_ElevenTwelveThirteenTakeTh(int zeroBasedIndex, string ordinal)
    {
        var many = Enumerable.Range(0, 30).Select(_ => Guid.NewGuid()).ToArray();

        Assert.Equal($"{ordinal} of 30 on the page", InstitutionPlacement.Of(many, many[zeroBasedIndex]).Phrase);
    }

    [Fact]
    public void ABankThatIsNotOnThePageIsTreatedAsNowhere_NothingMoves()
    {
        var placement = InstitutionPlacement.Of(Five, Guid.NewGuid());

        Assert.False(placement.CanMoveUp);
        Assert.False(placement.CanMoveDown);
        Assert.False(placement.CanMoveToBottom);
    }

    [Theory]
    [InlineData(4, "4 accounts · first on the page")] // mockup 2, verbatim
    [InlineData(1, "1 account · first on the page")]
    public void TheSubtitleIsTheAccountCountThenThePlace(int accounts, string expected) =>
        Assert.Equal(expected, InstitutionPlacement.Of(Five, Five[0]).Subtitle(accounts));
}
