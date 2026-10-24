using System.Text.Json;
using Constitution.Domain.Meals;
using Constitution.Domain.Recipes;
using static Constitution.Tests.MealTaste.TasteData;

namespace Constitution.Tests.MealTaste;

/// <summary>The profile as plain JSON (it is kept, never shown): frozen names, frozen words, and a reader that quotes nothing it refuses.</summary>
public class TasteProfileCodecTests
{
    private static TasteProfile Rich() => Build(
        [
            Rate(Sam, Risotto, TasteVerdict.Loved), Rate(Sam, Pasta, TasteVerdict.Loved), Rate(Sam, Omelette, TasteVerdict.Fine), Rate(Sam, Chicken, TasteVerdict.NeverAgain),
            Rate(Robin, Burritos, TasteVerdict.Loved), Rate(Robin, Tacos, TasteVerdict.Fine), Rate(Juniper, Pasta, TasteVerdict.NeverAgain),
        ],
        [
            .. TasteSeed.Notes(Lee, ["mushrooms", "Mexican"], Day(0)),
            Note(Lee, TasteSubjectKind.Dish, "onion-tart", TasteStance.Likes),
        ]);

    [Fact]
    public void AProfileRoundTripsToTheSameJson()
    {
        var json = TasteProfileCodec.Write(Rich());

        var read = Assert.IsType<TasteProfileRead.Read>(TasteProfileCodec.Read(json));

        Assert.Equal(json, TasteProfileCodec.Write(read.Profile));
        Assert.Equal(Rich().For(Sam)!.Cuisines[Cuisine.Italian], read.Profile.For(Sam)!.Cuisines[Cuisine.Italian]);
        Assert.Equal(Rich().For(Lee)!.Ingredients["mushroom"], read.Profile.For(Lee)!.Ingredients["mushroom"]);
        Assert.Equal(Rich().KidFriendly, read.Profile.KidFriendly);
        Assert.Equal([Chicken], read.Profile.For(Sam)!.NeverAgain);
        Assert.True(read.Profile.For(Robin)!.IsChild);
    }

    [Fact]
    public void AReadProfileScoresADishLikeTheOriginal()
    {
        var read = Assert.IsType<TasteProfileRead.Read>(TasteProfileCodec.Read(TasteProfileCodec.Write(Rich())));
        var card = Box().Single(recipe => recipe.Id == Burritos);

        Assert.Equal(Rich().For(Lee)!.Appetite(card), read.Profile.For(Lee)!.Appetite(card));
        Assert.Equal(Rich().For(Robin)!.Appetite(card), read.Profile.For(Robin)!.Appetite(card));
    }

    [Fact]
    public void TheFieldNamesAreFrozen()
    {
        using var document = JsonDocument.Parse(TasteProfileCodec.Write(Rich()));

        var root = document.RootElement;
        Assert.Equal(["kidFriendly", "people", "version"], root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        var sam = root.GetProperty("people").EnumerateArray().Single(p => p.GetProperty("person").GetString() == "sam");
        Assert.Equal(
            ["child", "cuisines", "dishes", "fine", "ingredients", "loved", "neverAgain", "person"],
            sam.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToList());
        Assert.Equal(["american", "french", "italian"], sam.GetProperty("cuisines").EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal("roast-chicken", sam.GetProperty("neverAgain")[0].GetString());
    }

    [Fact]
    public void AnEmptyProfileRoundTrips()
    {
        var read = Assert.IsType<TasteProfileRead.Read>(TasteProfileCodec.Read(TasteProfileCodec.Write(TasteProfile.Empty)));

        Assert.Empty(read.Profile.People);
    }

    [Fact]
    public void UnknownKeysAreIgnored()
    {
        var json = TasteProfileCodec.Write(Rich()).Replace("\"version\":1", "\"version\":1,\"somethingNew\":[1,2]", StringComparison.Ordinal);

        Assert.IsType<TasteProfileRead.Read>(TasteProfileCodec.Read(json));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    public void NotJsonIsUnreadable(string? json)
    {
        var read = Assert.IsType<TasteProfileRead.Unreadable>(TasteProfileCodec.Read(json));

        Assert.Equal(TasteProfileCodec.NotJson, read.Reason);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"version\":2,\"people\":[],\"kidFriendly\":[]}")]
    [InlineData("{\"version\":1,\"people\":{},\"kidFriendly\":[]}")]
    [InlineData("{\"version\":1,\"people\":[{\"person\":\"Not A Person\",\"child\":false}],\"kidFriendly\":[]}")]
    [InlineData("{\"version\":1,\"people\":[{\"person\":\"sam\",\"child\":false,\"cuisines\":{\"martian\":3}}],\"kidFriendly\":[]}")]
    [InlineData("{\"version\":1,\"people\":[{\"person\":\"sam\",\"child\":false,\"loved\":[\"BAD ID\"]}],\"kidFriendly\":[]}")]
    [InlineData("{\"version\":1,\"people\":[{\"person\":\"sam\",\"child\":false,\"ingredients\":{\"onion\":\"lots\"}}],\"kidFriendly\":[]}")]
    public void AShapeThisVersionDoesNotReadIsUnreadableAndQuotesNothing(string json)
    {
        var read = Assert.IsType<TasteProfileRead.Unreadable>(TasteProfileCodec.Read(json));

        Assert.Equal(TasteProfileCodec.WrongShape, read.Reason);
    }

    [Fact]
    public void TheJsonHoldsNoWrittenReasonAndNoPersonsName()
    {
        var json = TasteProfileCodec.Write(Build([Rate(Sam, Risotto, TasteVerdict.NeverAgain, reason: "tasted of soap")]));

        Assert.DoesNotContain("soap", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Sam", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritingIsDeterministic()
    {
        Assert.Equal(TasteProfileCodec.Write(Rich()), TasteProfileCodec.Write(Rich()));
    }
}
