using System.Text.RegularExpressions;
using Constitution.Domain.Recipes;
using Constitution.Domain.Voice;

namespace Constitution.Tests.Recipes;

/// <summary>The empty box, counting to ten (C-6.2.5, book p.14, D62, D91).</summary>
public class EmptyBoxTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    [InlineData(23, true)]
    public void PlanningWaitsForTen(int cards, bool planningMayStart)
    {
        Assert.Equal(planningMayStart, EmptyBox.For(cards).PlanningMayStart);
        Assert.Equal(!planningMayStart, EmptyBox.For(cards).ShowsInvitation);
    }

    [Fact]
    public void TheTargetIsTen()
    {
        Assert.Equal(10, EmptyBox.Target);
        Assert.Equal(10, EmptyBox.For(0).Places.Count);
    }

    [Theory]
    [InlineData(0, "0 of 10")]
    [InlineData(3, "3 of 10")]
    [InlineData(9, "9 of 10")]
    public void TheCounterReadsXOfTen(int cards, string counter)
    {
        Assert.Equal(counter, EmptyBox.For(cards).Counter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(40)]
    public void TenPlacesFilledFirstThenDashed(int cards)
    {
        var places = EmptyBox.For(cards).Places;
        var filled = Math.Min(cards, 10);

        Assert.Equal(10, places.Count);
        Assert.Equal(Enumerable.Repeat(PlaceState.Filled, filled).Concat(Enumerable.Repeat(PlaceState.Dashed, 10 - filled)), places);
    }

    [Fact]
    public void TheWordsAreThePagesWords()
    {
        var state = EmptyBox.For(3);

        Assert.Equal("Ten family favourites, and the planning can begin.", state.Invitation);
        Assert.Equal("Weekly meal sheets start on the Thursday after the tenth card.", state.Footnote);
        Assert.Equal("Scan a recipe card", state.PrimaryAction);
    }

    [Fact]
    public void ThePrimaryActionChangesAtTen()
    {
        Assert.Equal("Scan a recipe card", EmptyBox.For(9).PrimaryAction);
        Assert.Equal("Add a recipe", EmptyBox.For(10).PrimaryAction);
    }

    [Fact]
    public void NoticeAndBookmarkWaitForTen()
    {
        Assert.True(EmptyBox.For(0).WaitsWithReferenceNotice);
        Assert.True(EmptyBox.For(9).WaitsWithReferenceNotice);
        Assert.True(EmptyBox.For(9).BookmarkWaits);
        Assert.False(EmptyBox.For(10).WaitsWithReferenceNotice);
        Assert.False(EmptyBox.For(10).BookmarkWaits);
    }

    [Fact]
    public void ABoxCanBeCountedDirectly()
    {
        var box = new RecipeBox(RecipeData.Box().Take(3));

        Assert.Equal("3 of 10", EmptyBox.For(box).Counter);
        Assert.True(EmptyBox.For(new RecipeBox(RecipeData.Box())).PlanningMayStart);
    }

    [Fact]
    public void ANegativeCountIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => EmptyBox.For(-1));
    }

    [Fact]
    public void TheInvitationIsNotAVoiceSeat()
    {
        // The book says so (p.14: "it isn't one of the six voice seats"); the seats are a closed list.
        Assert.Equal(6, Enum.GetValues<VoiceSeat>().Length);
        Assert.DoesNotContain(VoicedOccasions.All, occasion => occasion.Purpose.Contains("invitation", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(VoicedOccasions.All, occasion => occasion.Purpose.Contains("recipe box", StringComparison.OrdinalIgnoreCase));

        var file = ConstitutionSource.Files(".cs").Single(path => ConstitutionSource.IsGuard(path, "EmptyBox.cs"));
        var code = Regex.Replace(File.ReadAllText(file), @"///.*|//.*", string.Empty);
        Assert.DoesNotContain("Voice", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Claude", code, StringComparison.Ordinal);
    }
}
