using System.Text.RegularExpressions;
using Constitution.Domain.Recipes;

namespace Constitution.Tests.Recipes;

/// <summary>The closed list of cuisines (D53, D60), with the words it is stored in frozen.</summary>
public class CuisineTests
{
    [Fact]
    public void StoredWordsAreFrozen()
    {
        var expected = new Dictionary<Cuisine, string>
        {
            [Cuisine.American] = "american",
            [Cuisine.Asian] = "asian",
            [Cuisine.French] = "french",
            [Cuisine.Indian] = "indian",
            [Cuisine.Italian] = "italian",
            [Cuisine.Mediterranean] = "mediterranean",
            [Cuisine.Mexican] = "mexican",
            [Cuisine.Other] = "other",
        };

        Assert.Equal(expected.Count, Enum.GetValues<Cuisine>().Length);
        Assert.All(expected, pair => Assert.Equal(pair.Value, Cuisines.Words.Word(pair.Key)));
    }

    [Fact]
    public void WordsReadBackToTheirCuisine()
    {
        foreach (var cuisine in Cuisines.All)
        {
            Assert.True(Cuisines.Words.TryValue(Cuisines.Words.Word(cuisine), out var back));
            Assert.Equal(cuisine, back);
        }

        Assert.False(Cuisines.Words.TryValue("Italian", out _));
        Assert.False(Cuisines.Words.TryValue("thai", out _));
    }

    [Fact]
    public void OtherIsTheCatchAllAndComesLast()
    {
        Assert.Equal(Cuisine.Other, Cuisines.All[^1]);
        Assert.Equal(Enum.GetValues<Cuisine>().Length, Cuisines.All.Count);
        Assert.Equal(Cuisines.All.Count, Cuisines.All.Distinct().Count());
    }

    [Fact]
    public void LabelsAreTheNamesOnTheStrip()
    {
        Assert.Equal("Italian", Cuisines.Label(Cuisine.Italian));
        Assert.Equal("Mediterranean", Cuisines.Label(Cuisine.Mediterranean));
        Assert.Equal("Other", Cuisines.Label(Cuisine.Other));
    }

    [Fact]
    public void WordsAreLiteralsNeverNameof()
    {
        var file = ConstitutionSource.Files(".cs").Single(path => ConstitutionSource.IsGuard(path, "Cuisines.cs"));
        var code = Regex.Replace(File.ReadAllText(file), @"///.*|//.*", string.Empty);

        Assert.DoesNotContain("nameof(", code, StringComparison.Ordinal);
        Assert.DoesNotContain(".ToString()", code, StringComparison.Ordinal);
    }
}
